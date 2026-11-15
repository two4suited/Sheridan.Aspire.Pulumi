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

    [Fact]
    public void Infrastructure_that_reaches_an_app_through_a_variable_is_deferred()
    {
        var p = new PulumiProgram();
        p.Resources["storage"] = Res("azure-native:storage:StorageAccount");
        p.AddAppResource("apiApp", Res("azure-native:app:ContainerApp"));
        p.Variables["apiHost"] = "${apiApp.configuration.ingress.fqdn}";
        p.Resources["backend"] = Res("azure-native:apimanagement:Backend", new() { ["url"] = "https://${apiHost}" });

        Assert.Equal(["backend"], p.GetDeferredResources());
        Assert.Equal(["storage"], p.GetInfrastructureTargets().Select(t => t.Name));
        var yaml = p.ToProvisionYaml("split", "test")!;
        Assert.DoesNotContain("apiHost", yaml);
        Assert.DoesNotContain("backend:", yaml);
        Assert.Contains("  apiApp:", yaml);
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
        p.AddInfraResource("backend", Random, Res(Random, new() { ["byteLength"] = 4, ["prefix"] = "https://${apiApp.keepers.fqdn}/${storage.hex}" }));
        p.Resources["policy"] = Res(Random, new() { ["byteLength"] = 4, ["prefix"] = "${storage.hex}" }, new() { ["dependsOn"] = new List<object?> { "${backend}" } });
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

            // Control: with the full program the same targeted update fails, although the backend is not targeted.
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => Pulumi(dir, env, provision.ToArray()));
            Assert.Contains("receiver must be a list or object, not nil", failure.Message);

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
            Assert.StartsWith("https://api.example/", (await Pulumi(dir, env, "stack", "output", "backendUrl")).Trim());

            // Control (issue #228): a redeploy's provision pass with the reduced program and without targeting the app-dependent
            // resources, which are in state, makes Pulumi refuse to delete them.
            await File.WriteAllTextAsync(program, p.ToProvisionYaml("split", "test"));
            var refusal = await Assert.ThrowsAnyAsync<Exception>(() => Pulumi(dir, env, provision.ToArray()));
            Assert.Contains("will be destroyed but was not specified in --target list", refusal.Message);

            // Redeploy, apps in state: the planner keeps the full program and targets the app-dependent resources.
            async Task<string> Redeploy()
            {
                var state = PulumiExtensions.ParseStateResources(await Pulumi(dir, env, "stack", "export"));
                var plan = p.PlanProvision("split", "test", state);
                var args = provision.ToList();
                foreach (var (type, name) in plan.ExtraTargets) args.AddRange(["--target", $"urn:pulumi:dev::split::{type}::{name}"]);
                await File.WriteAllTextAsync(program, plan.ProvisionYaml ?? p.ToYaml("split", "test"));
                await Pulumi(dir, env, args.ToArray());
                return await Pulumi(dir, env, "stack", "--show-urns");
            }
            urns = await Redeploy();
            foreach (var name in new[] { "storage", "literalBackend", "apiApp", "backend", "policy" })
                Assert.Contains($"::{name}", urns);

            // Redeploy, the app is missing from state while its dependents are still there: they are removed (and recreated
            // by the deploy pass) instead of failing the update.
            var exported = System.Text.Json.Nodes.JsonNode.Parse(await Pulumi(dir, env, "stack", "export"))!;
            var resources = exported["deployment"]!["resources"]!.AsArray();
            var appUrn = resources.Single(r => r!["urn"]!.GetValue<string>().EndsWith("::apiApp"))!["urn"]!.GetValue<string>();
            resources.Remove(resources.Single(r => r!["urn"]!.GetValue<string>() == appUrn));
            foreach (var r in resources)
            {
                // Forget the app wherever the others recorded a dependency on it, so the edited state stays consistent.
                if (r!["dependencies"] is System.Text.Json.Nodes.JsonArray deps)
                    foreach (var d in deps.Where(d => d!.GetValue<string>() == appUrn).ToList()) deps.Remove(d);
                if (r["propertyDependencies"] is System.Text.Json.Nodes.JsonObject pd)
                    foreach (var (_, v) in pd)
                        if (v is System.Text.Json.Nodes.JsonArray a)
                            foreach (var d in a.Where(d => d!.GetValue<string>() == appUrn).ToList()) a.Remove(d);
            }
            var statePath = Path.Combine(dir, "state.json");
            await File.WriteAllTextAsync(statePath, exported.ToJsonString());
            await Pulumi(dir, env, "stack", "import", "--file", statePath);
            urns = await Redeploy();
            Assert.DoesNotContain("::apiApp", urns);
            Assert.DoesNotContain("::backend", urns);
            Assert.DoesNotContain("::policy", urns);
            Assert.Contains("::storage", urns);
            await File.WriteAllTextAsync(program, p.ToYaml("split", "test"));
            await Pulumi(dir, env, "up", "--yes", "--skip-preview", "--non-interactive");
            Assert.Contains("::backend", await Pulumi(dir, env, "stack", "--show-urns"));
        }
        finally
        {
            Directory.Delete(dir, true);
            Directory.Delete(backend, true);
        }
    }

    [Fact]
    public void State_resources_are_read_as_type_and_name_from_a_stack_export()
    {
        const string Export = """{"version":3,"deployment":{"resources":[{"urn":"urn:pulumi:dev::p::pulumi:pulumi:Stack::p-dev"},{"urn":"urn:pulumi:dev::p::azure-native:apimanagement:Backend::apimBackend"},{"urn":"urn:pulumi:dev::p::a:b:Parent$a:b:Child::c"}]}}""";
        Assert.Equal(["a:b:Child::c", "azure-native:apimanagement:Backend::apimBackend", "pulumi:pulumi:Stack::p-dev"], PulumiExtensions.ParseStateResources(Export).Order());
        Assert.Empty(PulumiExtensions.ParseStateResources(""));
        Assert.Empty(PulumiExtensions.ParseStateResources("""{"version":3,"deployment":{}}"""));
        Assert.Empty(PulumiExtensions.ParseStateResources("""{"version":3,"deployment":{"resources":null}}"""));
    }

    private static PulumiProgram TwoApps()
    {
        var p = new PulumiProgram();
        p.Resources["infra"] = Res("t:x:Infra", []);
        p.AddAppResource("appA", Res("t:x:App", []));
        p.AddAppResource("appB", Res("t:x:App", []));
        p.AddInfraResource("backendA", "t:x:Backend", Res("t:x:Backend", new() { ["url"] = "${appA.fqdn}" }));
        p.AddInfraResource("backendB", "t:x:Backend", Res("t:x:Backend", new() { ["url"] = "${appB.fqdn}" }));
        return p;
    }

    [Fact]
    public void Plan_keeps_the_full_program_and_targets_app_dependent_resources_when_every_app_is_in_state()
    {
        var plan = TwoApps().PlanProvision("p", "d", new HashSet<string> { "t:x:Infra::infra", "t:x:App::appA", "t:x:App::appB", "t:x:Backend::backendA", "t:x:Backend::backendB" });
        Assert.Null(plan.ProvisionYaml);
        Assert.Equal(["backendA", "backendB"], plan.ExtraTargets.Select(t => t.Name).Order());
    }

    [Fact]
    public void Plan_only_removes_the_resources_that_depend_on_a_missing_app()
    {
        var plan = TwoApps().PlanProvision("p", "d", new HashSet<string> { "t:x:Infra::infra", "t:x:App::appA", "t:x:Backend::backendA", "t:x:Backend::backendB" });
        Assert.DoesNotContain("backendB", plan.ProvisionYaml);
        Assert.Contains("backendA", plan.ProvisionYaml);
        Assert.Equal(["backendA", "backendB"], plan.ExtraTargets.Select(t => t.Name).Order());
    }

    [Fact]
    public void Plan_does_not_target_omitted_resources_that_are_not_in_state_and_matches_on_type()
    {
        // First deploy: nothing in state, so nothing is targeted and every app-dependent resource is left out.
        var first = TwoApps().PlanProvision("p", "d", new HashSet<string>());
        Assert.Empty(first.ExtraTargets);
        Assert.DoesNotContain("backend", first.ProvisionYaml);

        // An app of the same name but another type counts as missing.
        var retyped = TwoApps().PlanProvision("p", "d", new HashSet<string> { "t:x:Other::appA", "t:x:App::appB", "t:x:Backend::backendA", "t:x:Backend::backendB" });
        Assert.DoesNotContain("backendA", retyped.ProvisionYaml);
        Assert.Contains("backendB", retyped.ProvisionYaml);
        Assert.Equal(["backendA", "backendB"], retyped.ExtraTargets.Select(t => t.Name).Order());
    }

    [Fact]
    public void Stale_urns_are_the_state_resources_the_program_no_longer_declares()
    {
        var p = new PulumiProgram();
        p.Resources["keep"] = Res("t:x:Keep");
        var json = """
        { "deployment": { "resources": [
          { "urn": "urn:pulumi:dev::proj::pulumi:pulumi:Stack::proj-dev" },
          { "urn": "urn:pulumi:dev::proj::pulumi:providers:azure-native::default_1" },
          { "urn": "urn:pulumi:dev::proj::t:x:Keep::keep" },
          { "urn": "urn:pulumi:dev::proj::t:x:Op::removedOp" },
          { "urn": "urn:pulumi:dev::proj::t:x:Keep::renamed" },
          { "urn": "urn:pulumi:dev::proj::t:x:Keep$t:x:Child::liveChild", "parent": "urn:pulumi:dev::proj::t:x:Keep::keep" },
          { "urn": "urn:pulumi:dev::proj::t:x:Gone::gone" },
          { "urn": "urn:pulumi:dev::proj::t:x:Gone$t:x:Child::goneChild", "parent": "urn:pulumi:dev::proj::t:x:Gone::gone" }
        ] } }
        """;
        Assert.Equal(
            ["urn:pulumi:dev::proj::t:x:Op::removedOp", "urn:pulumi:dev::proj::t:x:Keep::renamed", "urn:pulumi:dev::proj::t:x:Gone::gone", "urn:pulumi:dev::proj::t:x:Gone$t:x:Child::goneChild"],
            PulumiExtensions.ParseStaleUrns(json, p.DeclaresResource));
        Assert.Empty(PulumiExtensions.ParseStaleUrns("", p.DeclaresResource));
    }

    /// <summary>Issue #236: a resource removed from the model between two deploys must be targeted by the provision pass, or Pulumi refuses to delete it.</summary>
    [Fact]
    public async Task Provision_pass_deletes_a_resource_removed_from_the_model()
    {
        if (!PulumiOnPath()) return;

        var p = new PulumiProgram();
        p.Resources["storage"] = Res("random:index/randomId:RandomId", new() { ["byteLength"] = 4 });
        // Like an APIM operation under its API: it references a targeted resource, so Pulumi insists on targeting its deletion.
        p.Resources["operation"] = Res("random:index/randomId:RandomId", new() { ["byteLength"] = 4, ["keepers"] = new Dictionary<string, object?> { ["s"] = "${storage.hex}" } });
        p.AddAppResource("apiApp", Res("random:index/randomId:RandomId", new() { ["byteLength"] = 4 }));

        var dir = Directory.CreateTempSubdirectory("pulumi-split-").FullName;
        var backend = Directory.CreateTempSubdirectory("pulumi-state-").FullName;
        try
        {
            var env = new Dictionary<string, string>
            {
                ["PULUMI_BACKEND_URL"] = "file://" + backend,
                ["PULUMI_CONFIG_PASSPHRASE"] = "x",
                ["PULUMI_SKIP_UPDATE_CHECK"] = "true",
            };
            await File.WriteAllTextAsync(Path.Combine(dir, "Pulumi.yaml"), p.ToYaml("split", "test"));
            await Pulumi(dir, env, "stack", "init", "dev");
            await Pulumi(dir, env, "up", "--yes", "--skip-preview", "--non-interactive");

            // The next model no longer has the operation.
            p.Resources.Remove("operation");
            await File.WriteAllTextAsync(Path.Combine(dir, "Pulumi.yaml"), p.ToYaml("split", "test"));

            var args = new List<string> { "up", "--yes", "--skip-preview", "--non-interactive" };
            foreach (var (type, name) in p.GetInfrastructureTargets())
                args.AddRange(["--target", $"urn:pulumi:dev::split::{type}::{name}"]);

            var refusal = await Assert.ThrowsAnyAsync<Exception>(() => Pulumi(dir, env, args.ToArray()));
            Assert.Contains("will be destroyed but was not specified in --target list", refusal.Message);

            foreach (var urn in PulumiExtensions.ParseStaleUrns(await Pulumi(dir, env, "stack", "export"), p.DeclaresResource))
                args.AddRange(["--target", urn]);
            await Pulumi(dir, env, args.ToArray());

            var urns = await Pulumi(dir, env, "stack", "--show-urns");
            Assert.Contains("::storage", urns);
            Assert.Contains("::apiApp", urns);
            Assert.DoesNotContain("::operation", urns);
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
