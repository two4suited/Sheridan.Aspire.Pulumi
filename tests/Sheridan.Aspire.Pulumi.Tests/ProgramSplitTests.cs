using System.Diagnostics;
using Sheridan.Aspire.Pulumi;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

/// <summary>The infrastructure/app split that drives the provision pass of a deploy (see PulumiProgram.GetInfrastructureTargets).</summary>
public sealed class ProgramSplitTests
{
    private static Dictionary<string, object?> Res(string type, Dictionary<string, object?>? props = null, Dictionary<string, object?>? options = null)
    {
        var d = new Dictionary<string, object?> { ["type"] = type };
        if (props is not null) d["properties"] = props;
        if (options is not null) d["options"] = options;
        return d;
    }

    [Fact]
    public void Resources_are_infrastructure_by_default()
    {
        var p = new PulumiProgram();
        p.Resources["cosmos"] = Res("azure-native:cosmosdb:DatabaseAccount"); // set directly, like integrations that never call AddInfraResource
        p.AddInfraResource("env", "azure-native:app:ManagedEnvironment", Res("azure-native:app:ManagedEnvironment"));
        p.AddAppResource("apiApp", Res("azure-native:app:ContainerApp", new() { ["environmentId"] = "${env.id}" }));

        var targets = p.GetInfrastructureTargets().Select(t => t.Name).Order().ToList();
        Assert.Equal(["cosmos", "env"], targets);
        Assert.True(p.HasApps);
    }

    [Fact]
    public void Resources_that_reference_an_app_are_not_targeted_even_transitively()
    {
        var p = new PulumiProgram();
        p.Resources["storage"] = Res("azure-native:storage:StorageAccount");
        p.AddAppResource("webApp", Res("azure-native:web:WebApp"));
        p.Resources["webRole"] = Res("azure-native:authorization:RoleAssignment", new() { ["principalId"] = "${webApp.identity.principalId}" });
        p.Resources["webRoleLock"] = Res("azure-native:authorization:ManagementLockByScope", options: new() { ["dependsOn"] = new List<object?> { "${webRole}" } });
        p.Resources["literal"] = Res("azure-native:resources:ResourceGroup", new() { ["name"] = "$${webApp}" }); // escaped, not a reference

        Assert.Equal(["literal", "storage"], p.GetInfrastructureTargets().Select(t => t.Name).Order());
    }

    [Fact]
    public void App_scope_marks_new_resources_as_apps_but_keeps_explicit_infrastructure()
    {
        var p = new PulumiProgram();
        p.AddInfraResource("cluster", "azure-native:containerservice:ManagedCluster", Res("azure-native:containerservice:ManagedCluster"));
        using (p.BeginAppScope())
        {
            p.AddInfraResource("identity", "azure-native:managedidentity:UserAssignedIdentity", Res("azure-native:managedidentity:UserAssignedIdentity"));
            p.Resources["provider"] = Res("pulumi:providers:kubernetes");
            p.Resources["deployment"] = Res("kubernetes:apps/v1:Deployment");
        }

        Assert.Equal(["cluster", "identity"], p.GetInfrastructureTargets().Select(t => t.Name).Order());
        Assert.True(p.HasApps);
    }

    [Fact]
    public void Program_without_apps_has_no_provision_pass()
    {
        var p = new PulumiProgram();
        p.Resources["rg"] = Res("azure-native:resources:ResourceGroup");
        Assert.False(p.HasApps);
    }

    /// <summary>
    /// Runs the real pulumi CLI against a local file backend (no cloud credentials): a targeted 'up' of only the
    /// infrastructure must record the outputs the apps need, leave app outputs unset, and a later full 'up' must create the app.
    /// </summary>
    [Fact]
    public async Task Targeted_provision_pass_makes_infrastructure_outputs_available_before_the_apps_exist()
    {
        if (!PulumiOnPath()) return; // nothing to verify without the CLI

        var p = new PulumiProgram();
        p.Resources["storage"] = Res("random:index/randomId:RandomId", new() { ["byteLength"] = 4 });
        p.Resources["cosmos"] = Res("random:index/randomId:RandomId", new() { ["byteLength"] = 4 });
        p.AddAppResource("apiApp", Res("random:index/randomId:RandomId", new() { ["byteLength"] = 4, ["keepers"] = new Dictionary<string, object?> { ["s"] = "${storage.hex}" } }));
        p.Outputs["storageEndpoint"] = "${storage.hex}";
        p.Outputs["cosmosEndpoint"] = "${cosmos.hex}";
        p.Outputs["apiFqdn"] = "${apiApp.hex}";

        var dir = Directory.CreateTempSubdirectory("pulumi-split-").FullName;
        var backend = Directory.CreateTempSubdirectory("pulumi-state-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "Pulumi.yaml"), p.ToYaml("split", "test"));
            var env = new Dictionary<string, string>
            {
                ["PULUMI_BACKEND_URL"] = "file://" + backend,
                ["PULUMI_CONFIG_PASSPHRASE"] = "x",
                ["PULUMI_SKIP_UPDATE_CHECK"] = "true",
            };
            await Pulumi(dir, env, "stack", "init", "dev");

            var args = new List<string> { "up", "--yes", "--skip-preview", "--non-interactive" };
            foreach (var (type, name) in p.GetInfrastructureTargets())
                args.AddRange(["--target", $"urn:pulumi:dev::split::{type}::{name}"]);
            await Pulumi(dir, env, args.ToArray());

            Assert.NotEqual("null", (await Pulumi(dir, env, "stack", "output", "storageEndpoint")).Trim());
            Assert.NotEqual("null", (await Pulumi(dir, env, "stack", "output", "cosmosEndpoint")).Trim());
            Assert.Equal("null", (await Pulumi(dir, env, "stack", "output", "apiFqdn")).Trim()); // the app is not created yet

            await Pulumi(dir, env, "up", "--yes", "--skip-preview", "--non-interactive");
            Assert.NotEqual("null", (await Pulumi(dir, env, "stack", "output", "apiFqdn")).Trim());
        }
        finally
        {
            Directory.Delete(dir, true);
            Directory.Delete(backend, true);
        }
    }

    private static bool PulumiOnPath()
    {
        try { using var pr = Process.Start(new ProcessStartInfo("pulumi", "version") { RedirectStandardOutput = true, RedirectStandardError = true })!; pr.WaitForExit(); return pr.ExitCode == 0; }
        catch { return false; }
    }

    private static async Task<string> Pulumi(string dir, Dictionary<string, string> env, params string[] args)
    {
        var psi = new ProcessStartInfo("pulumi") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        foreach (var (k, v) in env) psi.Environment[k] = v;
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"pulumi {string.Join(' ', args)} failed: {await stderr}{await stdout}");
        return await stdout;
    }
}
