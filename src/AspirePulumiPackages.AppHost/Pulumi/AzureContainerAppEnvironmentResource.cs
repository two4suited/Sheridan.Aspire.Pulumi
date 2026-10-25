using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Microsoft.Extensions.Logging;

namespace AspirePulumiPackages.AppHost.Pulumi;

/// <summary>
/// An Azure Container Apps environment provisioned by Pulumi (azure-native): resource group, Log Analytics
/// workspace, container registry, pull identity and managed environment. Compute resources that target it
/// are built, pushed to the registry and deployed as container apps.
/// </summary>
#pragma warning disable ASPIRECOMPUTE002
public sealed class AzureContainerAppEnvironmentResource : Resource, IComputeEnvironmentResource, IContainerRegistry, IPulumiProgramContributor
{
    private const string AcrLoginTag = "acr-login";
    private const string AcrPullRoleId = "7f951dda-4ed3-4680-a7ca-43fe172d538d";

    public AzureContainerAppEnvironmentResource(string name) : base(name)
    {
        Annotations.Add(new PipelineStepAnnotation(_ => (IEnumerable<PipelineStep>)
        [
            new PipelineStep
            {
                Name = $"pulumi-prepare-{name}",
                Description = $"Targets compute resources at the Container Apps environment '{name}'",
                Action = PrepareDeploymentTargetsAsync,
                DependsOnSteps = [WellKnownPipelineSteps.ValidateComputeEnvironments],
                RequiredBySteps = [WellKnownPipelineSteps.BeforeStart],
            },
            new PipelineStep
            {
                Name = $"acr-login-{name}",
                Description = $"Log the container runtime in to the registry of '{name}'",
                Tags = [AcrLoginTag],
                Action = LoginToRegistryAsync,
            },
        ]));

        Annotations.Add(new PipelineConfigurationAnnotation(context =>
        {
            var stack = Stack ?? context.Model.Resources.OfType<PulumiStackResource>().SingleOrDefault();
            if (stack is null) return;

            var provision = context.GetSteps(stack, WellKnownPipelineTags.ProvisionInfrastructure);
            var deploy = context.GetSteps(stack, WellKnownPipelineTags.DeployCompute);
            var login = context.GetSteps(this, AcrLoginTag);
            login.DependsOn(provision);

            foreach (var r in context.Model.GetComputeResources().Where(IsTargeted))
            {
                var push = context.GetSteps(r, WellKnownPipelineTags.PushContainerImage);
                push.DependsOn(login);
                deploy.DependsOn(push);
            }

            foreach (var r in context.Model.GetBuildResources())
            {
                context.GetSteps(r, WellKnownPipelineTags.BuildCompute)
                    .RequiredBy(WellKnownPipelineSteps.Deploy)
                    .DependsOn(WellKnownPipelineSteps.DeployPrereq);
            }
        }));
    }

    public PulumiStackResource? Stack { get; set; }

    /// <summary>Azure region; when null, the stack's 'location' config value (default 'eastus') is used.</summary>
    public string? Location { get; set; }

    private PulumiStackResource? _resolvedStack;

    private string Id => string.Concat(
        Name.Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select((p, i) => i == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));

    private bool IsTargeted(IResource r) =>
        r.Annotations.OfType<DeploymentTargetAnnotation>().Any(a => a.ComputeEnvironment == this);

    private PulumiOutputReference Output(string suffix) =>
        new(_resolvedStack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet."), $"{Id}{suffix}");

    ReferenceExpression IContainerRegistry.Name => ReferenceExpression.Create($"{Output("RegistryName")}");

    ReferenceExpression IContainerRegistry.Endpoint => ReferenceExpression.Create($"{Output("RegistryLoginServer")}");

    ReferenceExpression? IContainerRegistry.Repository => null;

    public ReferenceExpression GetHostAddressExpression(EndpointReference endpointReference) =>
        ReferenceExpression.Create($"{endpointReference.Resource.Name.ToLowerInvariant()}");

    public ReferenceExpression GetEndpointPropertyExpression(EndpointReferenceExpression endpointReferenceExpression) =>
        ReferenceExpression.Create($"{endpointReferenceExpression.Endpoint.Resource.Name.ToLowerInvariant()}");

    private async Task LoginToRegistryAsync(PipelineStepContext ctx)
    {
        var stack = _resolvedStack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet.");
        var registry = await Output("RegistryName").GetValueAsync(ctx.CancellationToken);
        var subscription = await Output("SubscriptionId").GetValueAsync(ctx.CancellationToken);

        var psi = new System.Diagnostics.ProcessStartInfo("az") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "acr", "login", "--name", registry!, "--subscription", subscription! }) psi.ArgumentList.Add(a);
        using var process = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("Failed to start the az CLI. Is it installed?");
        var output = await process.StandardOutput.ReadToEndAsync(ctx.CancellationToken) + await process.StandardError.ReadToEndAsync(ctx.CancellationToken);
        await process.WaitForExitAsync(ctx.CancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"'az acr login' failed (run 'az login' first): {output}");
        ctx.Logger.LogInformation("Logged in to registry {Registry}", registry);
    }

    private Task PrepareDeploymentTargetsAsync(PipelineStepContext ctx)
    {
        if (!ctx.ExecutionContext.IsPublishMode) return Task.CompletedTask;

        _resolvedStack = Stack ?? ctx.Model.Resources.OfType<PulumiStackResource>().SingleOrDefault()
            ?? throw new InvalidOperationException($"Add a Pulumi stack with AddPulumiStack() to provision '{Name}'.");

        foreach (var r in ctx.Model.GetComputeResources())
        {
            var explicitEnv = r.GetComputeEnvironment();
            if (explicitEnv is not null && explicitEnv != this) continue;
            if (IsTargeted(r)) continue;

            r.Annotations.Add(new DeploymentTargetAnnotation(new PulumiContainerAppResource(r.Name, r))
            {
                ContainerRegistry = this,
                ComputeEnvironment = this,
            });

#pragma warning disable ASPIREPIPELINES003
            r.Annotations.Add(new ContainerImagePushOptionsCallbackAnnotation(c =>
            {
                c.Options.RemoteImageName = r.Name.ToLowerInvariant();
                c.Options.RemoteImageTag = _resolvedStack.DeployTag;
            }));
#pragma warning restore ASPIREPIPELINES003
        }

        return Task.CompletedTask;
    }

    public async Task ContributeAsync(PulumiProgram program, PipelineStepContext ctx)
    {
        var stack = _resolvedStack ?? Stack ?? ctx.Model.Resources.OfType<PulumiStackResource>().Single();
        var location = Location ?? "${location}";
        var rg = $"${{{Id}Rg.name}}";
        program.Configuration.TryAdd("location", new Dictionary<string, object?> { ["type"] = "string", ["default"] = "eastus" });

        program.AddInfraResource($"{Id}Rg", "azure-native:resources:ResourceGroup", new()
        {
            ["type"] = "azure-native:resources:ResourceGroup",
            ["properties"] = new Dictionary<string, object?> { ["location"] = location },
        });

        program.AddInfraResource($"{Id}Logs", "azure-native:operationalinsights:Workspace", new()
        {
            ["type"] = "azure-native:operationalinsights:Workspace",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = rg,
                ["location"] = location,
                ["retentionInDays"] = 30,
                ["sku"] = new Dictionary<string, object?> { ["name"] = "PerGB2018" },
            },
        });

        program.Variables[$"{Id}LogKeys"] = new Dictionary<string, object?>
        {
            ["fn::invoke"] = new Dictionary<string, object?>
            {
                ["function"] = "azure-native:operationalinsights:getSharedKeys",
                ["arguments"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["workspaceName"] = $"${{{Id}Logs.name}}",
                },
            },
        };

        program.AddInfraResource($"{Id}Registry", "azure-native:containerregistry:Registry", new()
        {
            ["type"] = "azure-native:containerregistry:Registry",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = rg,
                ["location"] = location,
                ["sku"] = new Dictionary<string, object?> { ["name"] = "Basic" },
                ["adminUserEnabled"] = false,
            },
        });

        program.AddInfraResource($"{Id}Identity", "azure-native:managedidentity:UserAssignedIdentity", new()
        {
            ["type"] = "azure-native:managedidentity:UserAssignedIdentity",
            ["properties"] = new Dictionary<string, object?> { ["resourceGroupName"] = rg, ["location"] = location },
        });

        program.Variables["clientConfig"] = new Dictionary<string, object?>
        {
            ["fn::invoke"] = new Dictionary<string, object?>
            {
                ["function"] = "azure-native:authorization:getClientConfig",
                ["arguments"] = new Dictionary<string, object?>(),
            },
        };

        program.AddInfraResource($"{Id}AcrPull", "azure-native:authorization:RoleAssignment", new()
        {
            ["type"] = "azure-native:authorization:RoleAssignment",
            ["properties"] = new Dictionary<string, object?>
            {
                ["principalId"] = $"${{{Id}Identity.principalId}}",
                ["principalType"] = "ServicePrincipal",
                ["roleDefinitionId"] = $"/subscriptions/${{clientConfig.subscriptionId}}/providers/Microsoft.Authorization/roleDefinitions/{AcrPullRoleId}",
                ["scope"] = $"${{{Id}Registry.id}}",
            },
        });

        program.AddInfraResource(Id, "azure-native:app:ManagedEnvironment", new()
        {
            ["type"] = "azure-native:app:ManagedEnvironment",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = rg,
                ["location"] = location,
                ["appLogsConfiguration"] = new Dictionary<string, object?>
                {
                    ["destination"] = "log-analytics",
                    ["logAnalyticsConfiguration"] = new Dictionary<string, object?>
                    {
                        ["customerId"] = $"${{{Id}Logs.customerId}}",
                        ["sharedKey"] = $"${{{Id}LogKeys.primarySharedKey}}",
                    },
                },
            },
        });

        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";
        program.Outputs[$"{Id}DefaultDomain"] = $"${{{Id}.defaultDomain}}";
        program.Outputs[$"{Id}ResourceGroup"] = rg;
        program.Outputs[$"{Id}SubscriptionId"] = "${clientConfig.subscriptionId}";
        program.Outputs[$"{Id}RegistryName"] = $"${{{Id}Registry.name}}";
        program.Outputs[$"{Id}RegistryLoginServer"] = $"${{{Id}Registry.loginServer}}";

        foreach (var r in ctx.Model.GetComputeResources().Where(IsTargeted))
        {
            program.HasApps = true;
            program.Resources[AppId(r)] = await BuildContainerAppAsync(r, stack, ctx);
        }
    }

    private static string AppId(IResource r) => string.Concat(
        r.Name.Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select((p, i) => i == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..])) + "App";

    private async Task<Dictionary<string, object?>> BuildContainerAppAsync(IResource r, PulumiStackResource stack, PipelineStepContext ctx)
    {
        var rg = $"${{{Id}Rg.name}}";
        var appName = r.Name.ToLowerInvariant();

        var envVars = new Dictionary<string, object>();
        var envContext = new EnvironmentCallbackContext(ctx.ExecutionContext, r, envVars, ctx.CancellationToken);
        foreach (var a in r.Annotations.OfType<EnvironmentCallbackAnnotation>())
            await a.Callback(envContext);

        var httpEndpoints = r.TryGetEndpoints(out var eps)
            ? eps.Where(e => e.UriScheme is "http" or "https").ToList()
            : [];
        var targetPort = httpEndpoints.FirstOrDefault()?.TargetPort ?? 8080;
        var env = new List<object?>();
        foreach (var (key, value) in envVars)
        {
            // Endpoint-reference ports can't be resolved at publish time; the container app listens on the ingress target port.
            if (key is "HTTP_PORTS")
            {
                env.Add(new Dictionary<string, object?> { ["name"] = key, ["value"] = targetPort.ToString() });
                continue;
            }

            string? resolved;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                resolved = value switch
                {
                    string s => s,
                    IValueProvider p => await p.GetValueAsync(timeout.Token),
                    _ => value.ToString(),
                };
            }
            catch (Exception e) when (e is not OperationCanceledException || !ctx.CancellationToken.IsCancellationRequested)
            {
                ctx.Logger.LogWarning("Skipping environment variable {Key} for {Resource}: {Message}", key, r.Name, e.Message);
                continue;
            }

            if (resolved is null) continue;
            env.Add(new Dictionary<string, object?> { ["name"] = key, ["value"] = resolved });
        }

        var configuration = new Dictionary<string, object?>
        {
            ["activeRevisionsMode"] = "Single",
            ["registries"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["server"] = $"${{{Id}Registry.loginServer}}",
                    ["identity"] = $"${{{Id}Identity.id}}",
                },
            },
        };

        if (httpEndpoints.Count > 0)
        {
            configuration["ingress"] = new Dictionary<string, object?>
            {
                ["external"] = httpEndpoints.Any(e => e.IsExternal),
                ["targetPort"] = targetPort,
                ["transport"] = "auto",
                ["allowInsecure"] = false,
            };
        }

        return new()
        {
            ["type"] = "azure-native:app:ContainerApp",
            ["properties"] = new Dictionary<string, object?>
            {
                ["containerAppName"] = appName,
                ["resourceGroupName"] = rg,
                ["managedEnvironmentId"] = $"${{{Id}.id}}",
                ["identity"] = new Dictionary<string, object?>
                {
                    ["type"] = "UserAssigned",
                    ["userAssignedIdentities"] = new List<object?> { $"${{{Id}Identity.id}}" },
                },
                ["configuration"] = configuration,
                ["template"] = new Dictionary<string, object?>
                {
                    ["containers"] = new List<object?>
                    {
                        new Dictionary<string, object?>
                        {
                            ["name"] = appName,
                            ["image"] = $"${{{Id}Registry.loginServer}}/{appName}:{stack.DeployTag}",
                            ["env"] = env,
                        },
                    },
                    ["scale"] = new Dictionary<string, object?> { ["minReplicas"] = 1, ["maxReplicas"] = 1 },
                },
            },
            ["options"] = new Dictionary<string, object?>
            {
                ["dependsOn"] = new List<object?> { $"${{{Id}AcrPull}}" },
            },
        };
    }
}

/// <summary>Deployment target that represents the container app Pulumi creates for a compute resource.</summary>
public sealed class PulumiContainerAppResource(string name, IResource targetResource) : Resource(name)
{
    public IResource TargetResource { get; } = targetResource;
}
