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

    [Fact]
    public void Provision_program_is_the_full_program_unless_infrastructure_depends_on_an_app()
    {
        var p = new PulumiProgram();
        p.Resources["storage"] = Res("azure-native:storage:StorageAccount");
        p.AddAppResource("apiApp", Res("azure-native:app:ContainerApp", new() { ["storage"] = "${storage.name}" }));
        p.Outputs["apiFqdn"] = "${apiApp.configuration.ingress.fqdn}";

        Assert.Empty(p.GetDeferredResources());
        Assert.Null(p.ToProvisionYaml("split", "test"));
    }

    [Fact]
    public void Provision_program_omits_infrastructure_that_depends_on_an_app_and_everything_referencing_it()
    {
        var p = new PulumiProgram();
        p.Resources["storage"] = Res("azure-native:storage:StorageAccount");
        p.AddAppResource("apiApp", Res("azure-native:app:ContainerApp"));
        p.AddAppResource("workerApp", Res("azure-native:app:ContainerApp", new() { ["gateway"] = "${backend.name}" })); // an app that needs deferred infrastructure
        p.AddInfraResource("backend", "azure-native:apimanagement:Backend", Res("azure-native:apimanagement:Backend", new() { ["url"] = "https://${apiApp.configuration.ingress.fqdn}" }));
        p.Resources["policy"] = Res("azure-native:apimanagement:ApiPolicy", options: new() { ["dependsOn"] = new List<object?> { "${backend}" } });
        p.Variables["backendUrl"] = "${backend.url}";
        p.Variables["storageName"] = "${storage.name}";
        p.Outputs["storageOut"] = "${storageName}";
        p.Outputs["apiFqdn"] = "${apiApp.configuration.ingress.fqdn}";
        p.Outputs["backendId"] = "${backend.id}";
        p.Outputs["backendUrlOut"] = "${backendUrl}";
        p.Outputs["workerId"] = "${workerApp.id}";

        Assert.Equal(["backend", "policy"], p.GetDeferredResources().Order());
        Assert.Equal(["storage"], p.GetInfrastructureTargets().Select(t => t.Name));

        var yaml = p.ToProvisionYaml("split", "test")!;
        foreach (var kept in new[] { "  storage:", "  apiApp:", "  storageName:", "  storageOut:", "  apiFqdn:" }) // apps stay: they are just not targeted
            Assert.Contains(kept, yaml);
        foreach (var gone in new[] { "backend:", "policy:", "workerApp:", "backendUrl:", "backendId:", "backendUrlOut:", "workerId:", "${backend" })
            Assert.DoesNotContain(gone, yaml);

        var full = p.ToYaml("split", "test");
        foreach (var kept in new[] { "  backend:", "  policy:", "  workerApp:", "  backendUrl:", "  backendId:", "  workerId:" })
            Assert.Contains(kept, full);
    }

    /// <summary>
    /// Issue #204, against the real pulumi CLI: infrastructure that reads a nested property of an app (an API Management
    /// backend on a container app's ingress FQDN) cannot be evaluated before the app exists, even when it is not targeted.
    /// The provision pass must succeed without it, the deploy pass must create it, and a later provision pass (a redeploy)
    /// must not delete it.
    /// </summary>
    [Fact]
    public async Task Provision_pass_succeeds_when_infrastructure_reads_a_nested_property_of_an_app_that_does_not_exist_yet()
    {
        if (!PulumiOnPath()) return; // nothing to verify without the CLI

        const string Random = "random:index/randomId:RandomId";
        var p = new PulumiProgram();
        p.Resources["storage"] = Res(Random, new() { ["byteLength"] = 4 });
        p.Resources["literalBackend"] = Res(Random, new() { ["byteLength"] = 4, ["prefix"] = "https://example.com" });
        p.AddAppResource("apiApp", Res(Random, new() { ["byteLength"] = 4, ["keepers"] = new Dictionary<string, object?> { ["fqdn"] = "api.example" } }));
        p.AddInfraResource("backend", Random, Res(Random, new() { ["byteLength"] = 4, ["prefix"] = "https://${apiApp.keepers.fqdn}" }));
        p.Resources["policy"] = Res(Random, new() { ["byteLength"] = 4 }, new() { ["dependsOn"] = new List<object?> { "${backend}" } });
        p.Outputs["storageEndpoint"] = "${storage.hex}";
        p.Outputs["backendUrl"] = "${backend.prefix}";

        var dir = Directory.CreateTempSubdirectory("pulumi-split-").FullName;
        var backend = Directory.CreateTempSubdirectory("pulumi-state-").FullName;
        try
        {
            var program = Path.Combine(dir, "Pulumi.yaml");
            var env = new Dictionary<string, string>
            {
                ["PULUMI_BACKEND_URL"] = "file://" + backend,
                ["PULUMI_CONFIG_PASSPHRASE"] = "x",
                ["PULUMI_SKIP_UPDATE_CHECK"] = "true",
            };
            await File.WriteAllTextAsync(program, p.ToYaml("split", "test"));
            await Pulumi(dir, env, "stack", "init", "dev");

            Assert.Equal(["literalBackend", "storage"], p.GetInfrastructureTargets().Select(t => t.Name).Order());
            var provision = new List<string> { "up", "--yes", "--skip-preview", "--non-interactive" };
            foreach (var (type, name) in p.GetInfrastructureTargets())
                provision.AddRange(["--target", $"urn:pulumi:dev::split::{type}::{name}"]);

            // Provision pass, first deploy.
            await File.WriteAllTextAsync(program, p.ToProvisionYaml("split", "test"));
            await Pulumi(dir, env, provision.ToArray());
            var urns = await Pulumi(dir, env, "stack", "--show-urns");
            Assert.Contains("::storage", urns);
            Assert.Contains("::literalBackend", urns);
            Assert.DoesNotContain("::apiApp", urns);
            Assert.DoesNotContain("::backend", urns);
            Assert.NotEqual("null", (await Pulumi(dir, env, "stack", "output", "storageEndpoint")).Trim());

            // Deploy pass: the full program creates the app, then the backend and the policy that depend on it.
            await File.WriteAllTextAsync(program, p.ToYaml("split", "test"));
            await Pulumi(dir, env, "up", "--yes", "--skip-preview", "--non-interactive");
            Assert.Equal("https://api.example", (await Pulumi(dir, env, "stack", "output", "backendUrl")).Trim());

            // Provision pass of a redeploy: resources missing from the provision program are not targeted, so they survive.
            await File.WriteAllTextAsync(program, p.ToProvisionYaml("split", "test"));
            await Pulumi(dir, env, provision.ToArray());
            urns = await Pulumi(dir, env, "stack", "--show-urns");
            foreach (var name in new[] { "storage", "literalBackend", "apiApp", "backend", "policy" })
                Assert.Contains($"::{name}", urns);
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
