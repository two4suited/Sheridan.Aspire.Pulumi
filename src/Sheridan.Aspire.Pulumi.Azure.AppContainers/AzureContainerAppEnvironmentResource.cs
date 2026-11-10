using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Sheridan.Aspire.Pulumi.Azure.Base;
using Sheridan.Aspire.Pulumi.Azure.ContainerRegistry;
using Sheridan.Aspire.Pulumi.Azure.Network;
using Sheridan.Aspire.Pulumi.Azure.OperationalInsights;
using Microsoft.Extensions.Logging;

namespace Sheridan.Aspire.Pulumi.Azure.AppContainers;

/// <summary>
/// An Azure Container Apps environment provisioned by Pulumi (azure-native). Pulumi counterpart of the official
/// <c>AzureContainerAppEnvironmentResource</c>: resource group, Log Analytics workspace, container registry, ACR pull
/// identity (with AcrPull), consumption workload profile, Aspire dashboard and managed environment. Compute resources
/// that target it are built, pushed to the registry and deployed as container apps (or container jobs).
/// </summary>
#pragma warning disable ASPIRECOMPUTE002
public sealed class AzureContainerAppEnvironmentResource : PulumiAzureResource, IComputeEnvironmentResource, IContainerRegistry, IPulumiManagedIdentityResource
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

    /// <summary>Name of an existing managed environment (set by <c>AsExisting</c>/<c>PublishAsExisting</c>); when set, the environment itself is not created.</summary>
    public string? ExistingEnvironmentName { get; internal set; }

    /// <summary>Resource group of the existing managed environment; null means the resource group this resource is configured with.</summary>
    public string? ExistingEnvironmentResourceGroup { get; internal set; }

    /// <summary>A registry to use instead of the environment's own (see <c>WithAzureContainerRegistry</c>).</summary>
    public AzureContainerRegistryResource? Registry { get; set; }

    /// <summary>A Log Analytics workspace to use instead of the environment's own (see <c>WithAzureLogAnalyticsWorkspace</c>).</summary>
    public LogAnalyticsWorkspaceResource? LogAnalyticsWorkspace { get; set; }

    /// <summary>A user-assigned identity to use for ACR pulls instead of the environment's own (see <c>WithAcrPullIdentity</c>).</summary>
    public PulumiAzureUserAssignedIdentityResource? PullIdentity { get; set; }

    /// <summary>Whether the Aspire dashboard component is added to the environment (see <c>WithDashboard</c>); default true as upstream.</summary>
    public bool DashboardEnabled { get; set; } = true;

    /// <summary>True once <c>WithDashboard</c> was called explicitly.</summary>
    internal bool DashboardConfigured { get; set; }

    private PulumiStackResource? _resolvedStack;

    private string IdentityRef => PullIdentity?.Id ?? $"{Id}Identity";

    private string RegistryRef => Registry?.Id ?? $"{Id}Registry";

    private bool IsTargeted(IResource r) =>
        r.Annotations.OfType<DeploymentTargetAnnotation>().Any(a => a.ComputeEnvironment == this);

    private PulumiOutputReference EnvOutput(string suffix) =>
        Output(_resolvedStack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet."), suffix);

    /// <summary>Pulumi expression for this environment's shared managed identity's principal id; every app targeting it uses this one identity.</summary>
    public string PrincipalIdExpression => $"${{{IdentityRef}.principalId}}";

    public string? PrincipalNameExpression => $"${{{IdentityRef}.name}}";

    public string? ClientIdExpression => $"${{{IdentityRef}.clientId}}";

    ReferenceExpression IContainerRegistry.Name => ReferenceExpression.Create($"{EnvOutput("RegistryName")}");

    ReferenceExpression IContainerRegistry.Endpoint => ReferenceExpression.Create($"{EnvOutput("RegistryLoginServer")}");

    ReferenceExpression? IContainerRegistry.Repository => null;

    /// <summary>
    /// The container app (or job) name for a compute resource: its Aspire resource name, lower-cased, when that conforms to the
    /// Microsoft.App rules (2-32 lowercase letters, digits and hyphens, starting with a letter), otherwise a deterministic
    /// shortened name. Unique within the environment's resource group, so no project or stack segments are added.
    /// </summary>
    internal static string ContainerAppName(string computeResourceName, bool job = false) =>
        AzureNaming.ComposeScoped(job ? AzureNameKind.ContainerAppJob : AzureNameKind.ContainerApp, computeResourceName);

    public ReferenceExpression GetHostAddressExpression(EndpointReference endpointReference) =>
        ReferenceExpression.Create($"{ContainerAppName(endpointReference.Resource.Name)}");

    public ReferenceExpression GetEndpointPropertyExpression(EndpointReferenceExpression endpointReferenceExpression) =>
        ReferenceExpression.Create($"{ContainerAppName(endpointReferenceExpression.Endpoint.Resource.Name)}");

    private async Task LoginToRegistryAsync(PipelineStepContext ctx)
    {
        var registry = await EnvOutput("RegistryName").GetValueAsync(ctx.CancellationToken);
        var subscription = await EnvOutput("SubscriptionId").GetValueAsync(ctx.CancellationToken);

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

        _resolvedStack = ResolveStack(ctx);

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

    /// <summary>Rejects settings that can't apply to an existing environment.</summary>
    internal void ValidateExisting(string rg)
    {
        if (ExistingEnvironmentResourceGroup is null && !HasConfiguredResourceGroup)
            throw new InvalidOperationException(
                $"'{Name}' is an existing Container Apps environment but no resource group is known. Pass resourceGroupName to AsExisting/PublishAsExisting, or call WithExistingResourceGroup/WithResourceGroup.");
        if (DashboardConfigured && DashboardEnabled)
            throw new InvalidOperationException(
                $"'{Name}' is an existing Container Apps environment; the Aspire dashboard component can't be added to it. Remove WithDashboard.");
        if (Registry is null)
            throw new InvalidOperationException(
                $"'{Name}' is an existing Container Apps environment, so it has no registry of its own to push images to. " +
                "Call WithAzureContainerRegistry with a registry (use AsExisting on AddPulumiAzureContainerRegistry to reuse an existing one).");
        if (LogAnalyticsWorkspace is not null || Annotations.OfType<PulumiDelegatedSubnetAnnotation>().Any() || Annotations.OfType<PulumiPrivateLinkTargetAnnotation>().Any())
            throw new InvalidOperationException(
                $"'{Name}' is an existing Container Apps environment; its Log Analytics workspace, subnet and private link settings can't be changed from here.");
    }

    /// <summary>Emits the <c>getManagedEnvironment</c> lookup under the environment's id, so ${id}.id / .name / .defaultDomain / .location resolve as for a created environment.</summary>
    internal void AddExistingLookup(PulumiProgram program, string rg) =>
        program.Variables[Id] = new Dictionary<string, object?>
        {
            ["fn::invoke"] = new Dictionary<string, object?>
            {
                ["function"] = "azure-native:app:getManagedEnvironment",
                ["arguments"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = ExistingEnvironmentResourceGroup ?? rg,
                    ["environmentName"] = ExistingEnvironmentName,
                },
            },
        };

    protected override async Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext ctx, string rg, string location)
    {
        var stack = _resolvedStack ?? ResolveStack(ctx);
        RequireSameStack(Registry, stack, "container registry");
        RequireSameStack(LogAnalyticsWorkspace, stack, "Log Analytics workspace");
        RequireSameStack(PullIdentity, stack, "ACR pull identity");

        program.AddClientConfig();

        var existing = ExistingEnvironmentName is not null;
        if (existing) ValidateExisting(rg);

        // Log Analytics: the environment's own workspace (upstream '{name}-law') unless one was supplied.
        string lawName = "", lawId = "", lawCustomerId = "", lawResourceGroup = "";
        if (existing)
        {
            // The existing environment already has its logging configured.
        }
        else if (LogAnalyticsWorkspace is { } law)
        {
            lawName = $"${{{law.Id}.name}}";
            lawId = $"${{{law.Id}.id}}";
            lawCustomerId = $"${{{law.Id}.customerId}}";
            lawResourceGroup = law.ExistingResourceGroup ?? $"${{{law.Id}Rg.name}}";
        }
        else
        {
            program.AddInfraResource($"{Id}Logs", "azure-native:operationalinsights:Workspace", new()
            {
                ["type"] = "azure-native:operationalinsights:Workspace",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["workspaceName"] = NameFor(AzureNameKind.LogAnalyticsWorkspace, "law"),
                    ["resourceGroupName"] = rg,
                    ["location"] = location,
                    ["retentionInDays"] = 30,
                    ["sku"] = new Dictionary<string, object?> { ["name"] = "PerGB2018" },
                    ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = $"{Name}-law" },
                },
            });
            lawName = $"${{{Id}Logs.name}}";
            lawId = $"${{{Id}Logs.id}}";
            lawCustomerId = $"${{{Id}Logs.customerId}}";
            lawResourceGroup = rg;
        }

        if (!existing) program.Variables[$"{Id}LogKeys"] = new Dictionary<string, object?>
        {
            ["fn::invoke"] = new Dictionary<string, object?>
            {
                ["function"] = "azure-native:operationalinsights:getSharedKeys",
                ["arguments"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = lawResourceGroup,
                    ["workspaceName"] = lawName,
                },
            },
        };

        // Container registry: the environment's own (upstream '{name}-acr') unless one was supplied.
        if (Registry is null)
        {
            program.AddInfraResource($"{Id}Registry", "azure-native:containerregistry:Registry", new()
            {
                ["type"] = "azure-native:containerregistry:Registry",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["registryName"] = NameFor(AzureNameKind.ContainerRegistry, "acr"),
                    ["resourceGroupName"] = rg,
                    ["location"] = location,
                    ["sku"] = new Dictionary<string, object?> { ["name"] = "Basic" },
                    ["adminUserEnabled"] = false,
                    ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = $"{Name}-acr" },
                },
            });
        }

        // Pull identity: the environment's own managed identity (upstream '{name}-mi') unless one was supplied.
        if (PullIdentity is null)
        {
            program.AddInfraResource($"{Id}Identity", "azure-native:managedidentity:UserAssignedIdentity", new()
            {
                ["type"] = "azure-native:managedidentity:UserAssignedIdentity",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceName"] = NameFor(AzureNameKind.UserAssignedIdentity, "mi"),
                    ["resourceGroupName"] = rg,
                    ["location"] = location,
                    ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = $"{Name}-mi" },
                },
            });
        }

        program.AddRoleAssignment($"{Id}AcrPull", $"${{{IdentityRef}.principalId}}", AcrPullRoleId, $"${{{RegistryRef}.id}}");

        if (existing)
            AddExistingLookup(program, rg);
        else
        {
        var properties = new Dictionary<string, object?>
        {
            ["environmentName"] = NameFor(AzureNameKind.ContainerAppsEnvironment),
            ["resourceGroupName"] = rg,
            ["location"] = location,
            ["appLogsConfiguration"] = new Dictionary<string, object?>
            {
                ["destination"] = "log-analytics",
                ["logAnalyticsConfiguration"] = new Dictionary<string, object?>
                {
                    ["customerId"] = lawCustomerId,
                    ["sharedKey"] = $"${{{Id}LogKeys.primarySharedKey}}",
                },
            },
            ["workloadProfiles"] = new List<object?>
            {
                new Dictionary<string, object?> { ["name"] = "consumption", ["workloadProfileType"] = "Consumption" },
            },
            ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
        };

        // VNet integration: WithDelegatedSubnet (Pulumi.Azure.Network) supplies the infrastructure subnet.
        var subnet = Annotations.OfType<PulumiDelegatedSubnetAnnotation>().LastOrDefault()?.Subnet;
        if (subnet is not null)
        {
            properties["vnetConfiguration"] = new Dictionary<string, object?> { ["infrastructureSubnetId"] = subnet.IdExpression };
        }

        // Private Link from Front Door (WithAzureFrontDoorPrivateLink): the environment is only reachable privately.
        if (Annotations.OfType<PulumiPrivateLinkTargetAnnotation>().Any())
        {
            properties["publicNetworkAccess"] = "Disabled";
        }

        program.AddInfraResource(Id, "azure-native:app:ManagedEnvironment", new()
        {
            ["type"] = "azure-native:app:ManagedEnvironment",
            ["properties"] = properties,
        });

        if (DashboardEnabled)
        {
            program.Resources[$"{Id}Dashboard"] = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:app:DotNetComponent",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["name"] = "aspire-dashboard",
                    ["componentType"] = "AspireDashboard",
                    ["environmentName"] = $"${{{Id}.name}}",
                    ["resourceGroupName"] = rg,
                },
            };
        }

        }

        // Outputs mirror the upstream Bicep outputs (AZURE_CONTAINER_APPS_ENVIRONMENT_*, AZURE_CONTAINER_REGISTRY_*, AZURE_LOG_ANALYTICS_*).
        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        program.Outputs[$"{Id}DefaultDomain"] = $"${{{Id}.defaultDomain}}";
        program.Outputs[$"{Id}Location"] = existing ? $"${{{Id}.location}}" : location;
        program.Outputs[$"{Id}ResourceGroup"] = existing ? ExistingEnvironmentResourceGroup ?? rg : rg;
        program.Outputs[$"{Id}SubscriptionId"] = PulumiAzureProgramExtensions.SubscriptionId;
        program.Outputs[$"{Id}RegistryName"] = $"${{{RegistryRef}.name}}";
        program.Outputs[$"{Id}RegistryLoginServer"] = $"${{{RegistryRef}.loginServer}}";
        program.Outputs[$"{Id}RegistryManagedIdentityId"] = $"${{{IdentityRef}.id}}";
        if (!existing)
        {
            program.Outputs[$"{Id}LogAnalyticsWorkspaceName"] = lawName;
            program.Outputs[$"{Id}LogAnalyticsWorkspaceId"] = lawId;
        }

        foreach (var r in ctx.Model.GetComputeResources().Where(IsTargeted))
        {
            var job = r.Annotations.OfType<PulumiContainerAppJobAnnotation>().LastOrDefault();
            program.AddAppResource(AppId(r), await BuildContainerAppAsync(r, job, stack, rg, ctx, existing));
        }

        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
    }

    private static void RequireSameStack(PulumiAzureResource? other, PulumiStackResource stack, string what)
    {
        if (other?.Stack is { } s && s != stack)
            throw new InvalidOperationException(
                $"The {what} '{other.Name}' is in Pulumi stack '{s.Name}', but the Container Apps environment is in '{stack.Name}'; they must share a stack.");
    }

    private static string AppId(IResource r) => AppId(r.Name);

    /// <summary>Program id of the container app or job emitted for the compute resource with the given Aspire name.</summary>
    internal static string AppId(string computeResourceName) => PulumiAzureProgramExtensions.ToPulumiId(computeResourceName) + "App";

    private static List<EndpointAnnotation> HttpEndpoints(IResource r) =>
        r.TryGetEndpoints(out var eps) ? eps.Where(e => e.UriScheme is "http" or "https").ToList() : [];

    private static int IngressTargetPort(List<EndpointAnnotation> httpEndpoints) => httpEndpoints.FirstOrDefault()?.TargetPort ?? 8080;

    /// <summary>Resolves the environment variables of a compute resource into the container's <c>env</c> entries.</summary>
    internal async Task<List<object?>> BuildEnvironmentAsync(
        IResource r, DistributedApplicationExecutionContext executionContext, ILogger logger, PulumiStackResource? stack, CancellationToken cancellationToken)
    {
        var envVars = new Dictionary<string, object>();
        var envContext = new EnvironmentCallbackContext(executionContext, r, envVars, cancellationToken);
        foreach (var a in r.Annotations.OfType<EnvironmentCallbackAnnotation>())
            await a.Callback(envContext);

        var targetPort = IngressTargetPort(HttpEndpoints(r));
        var env = new List<object?>();
        foreach (var (key, value) in envVars)
        {
            // Endpoint-reference ports can't be resolved at publish time; the container app listens on the ingress target port.
            if (key is "HTTP_PORTS")
            {
                env.Add(new Dictionary<string, object?> { ["name"] = key, ["value"] = targetPort.ToString() });
                continue;
            }

            // TLS ends at the ingress, which forwards plain HTTP to the target port: the container must not also bind HTTPS there.
            if (key is "HTTPS_PORTS") continue;

            string? resolved;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                resolved = await ResolveValueAsync(value, timeout.Token);
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Outputs of infrastructure that doesn't exist yet resolve after the provision pass, when deploy regenerates the program.
                logger.LogDeferredEnvironmentVariable(stack, key, r.Name, e);
                continue;
            }

            if (resolved is null) continue;
            env.Add(new Dictionary<string, object?> { ["name"] = key, ["value"] = resolved });
        }

        // The app runs as the environment's user-assigned identity; DefaultAzureCredential only picks it when told its client id.
        if (!envVars.ContainsKey("AZURE_CLIENT_ID"))
            env.Add(new Dictionary<string, object?> { ["name"] = "AZURE_CLIENT_ID", ["value"] = $"${{{IdentityRef}.clientId}}" });

        return env;
    }

    /// <summary>
    /// Resolves an environment value. Endpoints of container apps in this environment (service discovery from
    /// <c>WithReference(project)</c>, <c>GetEndpoint(...)</c>) have no allocated address at publish time, so they are mapped to the
    /// app's ingress address the way the official Container Apps publisher does; everything else resolves through its value provider.
    /// </summary>
    private async Task<string?> ResolveValueAsync(object value, CancellationToken cancellationToken)
    {
        switch (value)
        {
            case string s:
                return s;
            case EndpointReference ep when TryGetEndpointValue(ep.Property(EndpointProperty.Url), out var url):
                return url;
            case EndpointReferenceExpression epExpr when TryGetEndpointValue(epExpr, out var endpointValue):
                return endpointValue;
            case ConnectionStringReference cs when ReferencesEnvironmentEndpoint(cs):
                return await ResolveValueAsync(cs.Resource.ConnectionStringExpression, cancellationToken);
            case IResourceWithConnectionString withConnectionString when ReferencesEnvironmentEndpoint(withConnectionString):
                return await ResolveValueAsync(withConnectionString.ConnectionStringExpression, cancellationToken);
            case ReferenceExpression { IsConditional: true } conditional when ReferencesEnvironmentEndpoint(conditional):
                var condition = await ResolveValueAsync(conditional.Condition!, cancellationToken);
                return await ResolveValueAsync(
                    string.Equals(condition, conditional.MatchValue, StringComparison.OrdinalIgnoreCase) ? conditional.WhenTrue! : conditional.WhenFalse!,
                    cancellationToken);
            case ReferenceExpression { IsConditional: false } expr when ReferencesEnvironmentEndpoint(expr) && expr.StringFormats.All(f => f is null):
                var args = new object?[expr.ValueProviders.Count];
                for (var i = 0; i < args.Length; i++)
                    args[i] = await ResolveValueAsync(expr.ValueProviders[i], cancellationToken);
                return string.Format(CultureInfo.InvariantCulture, expr.Format, args);
            case IValueProvider p:
                return await p.GetValueAsync(cancellationToken);
            default:
                return value.ToString();
        }
    }

    /// <summary>Whether an expression (at any depth) reads an endpoint of a container app in this environment.</summary>
    private bool ReferencesEnvironmentEndpoint(object? value) => value switch
    {
        EndpointReference ep => TryGetEndpointValue(ep.Property(EndpointProperty.Url), out _),
        EndpointReferenceExpression epExpr => TryGetEndpointValue(epExpr, out _),
        ConnectionStringReference cs => ReferencesEnvironmentEndpoint(cs.Resource.ConnectionStringExpression),
        IResourceWithConnectionString withConnectionString => ReferencesEnvironmentEndpoint(withConnectionString.ConnectionStringExpression),
        ReferenceExpression { IsConditional: true } c =>
            ReferencesEnvironmentEndpoint(c.Condition) || ReferencesEnvironmentEndpoint(c.WhenTrue) || ReferencesEnvironmentEndpoint(c.WhenFalse),
        ReferenceExpression expr => expr.ValueProviders.Any(ReferencesEnvironmentEndpoint),
        _ => false,
    };

    /// <summary>
    /// The publish-time value of an endpoint property of a container app in this environment, as the official Container Apps
    /// publisher maps it. HTTP(S) endpoints go through the app's ingress, which serves HTTPS on 443 (HTTP endpoints are upgraded;
    /// the ingress has <c>allowInsecure: false</c>): <c>{app}.internal.{defaultDomain}</c> for internal ingress and
    /// <c>{app}.{defaultDomain}</c> for external. Other endpoints are addressed by app name and port. False when the endpoint is not
    /// one this environment deploys (another compute environment, a job, an unknown endpoint), leaving it to its own value provider.
    /// </summary>
    internal bool TryGetEndpointValue(EndpointReferenceExpression reference, out string? value)
    {
        value = null;
        var endpoint = reference.Endpoint;
        var target = endpoint.Resource;
        if (!IsTargeted(target) || !endpoint.Exists || target.Annotations.OfType<PulumiContainerAppJobAnnotation>().Any()) return false;

        var annotation = endpoint.EndpointAnnotation;
        var host = ContainerAppName(target.Name);
        var httpEndpoints = HttpEndpoints(target);
        var isHttpIngress = annotation.UriScheme is "http" or "https";

        string scheme;
        int? port, targetPort;
        if (isHttpIngress)
        {
            var domain = $"${{{Id}.defaultDomain}}";
            host = httpEndpoints.Any(e => e.IsExternal) ? $"{host}.{domain}" : $"{host}.internal.{domain}";
            (scheme, port, targetPort) = ("https", 443, IngressTargetPort(httpEndpoints));
        }
        else
        {
            (scheme, port, targetPort) = (annotation.UriScheme, annotation.Port ?? annotation.TargetPort, annotation.TargetPort);
        }

        value = reference.Property switch
        {
            EndpointProperty.Url when isHttpIngress => $"{scheme}://{host}",
            EndpointProperty.Url when port is not null => $"{scheme}://{host}:{Port(port)}",
            EndpointProperty.Host or EndpointProperty.IPV4Host => host,
            EndpointProperty.Port when port is not null => Port(port),
            EndpointProperty.HostAndPort when port is not null => $"{host}:{Port(port)}",
            EndpointProperty.TargetPort when targetPort is not null => Port(targetPort),
            EndpointProperty.Scheme => scheme,
            EndpointProperty.TlsEnabled => isHttpIngress || annotation.TlsEnabled ? bool.TrueString : bool.FalseString,
            _ => null,
        };
        return value is not null;

        static string Port(int? p) => p!.Value.ToString(CultureInfo.InvariantCulture);
    }

    private async Task<Dictionary<string, object?>> BuildContainerAppAsync(
        IResource r, PulumiContainerAppJobAnnotation? job, PulumiStackResource stack, string rg, PipelineStepContext ctx, bool existingEnvironment)
    {
        var appName = ContainerAppName(r.Name, job is not null);
        var imageName = r.Name.ToLowerInvariant();

        var httpEndpoints = HttpEndpoints(r);
        var targetPort = IngressTargetPort(httpEndpoints);
        var env = await BuildEnvironmentAsync(r, ctx.ExecutionContext, ctx.Logger, stack, ctx.CancellationToken);

        var registries = new List<object?>
        {
            new Dictionary<string, object?>
            {
                ["server"] = $"${{{RegistryRef}.loginServer}}",
                ["identity"] = $"${{{IdentityRef}.id}}",
            },
        };

        var container = new Dictionary<string, object?>
        {
            ["name"] = appName,
            ["image"] = $"${{{RegistryRef}.loginServer}}/{imageName}:{stack.DeployTag}",
            ["env"] = env,
        };

        var identity = new Dictionary<string, object?>
        {
            ["type"] = "UserAssigned",
            ["userAssignedIdentities"] = new List<object?> { $"${{{IdentityRef}.id}}" },
        };

        var options = new Dictionary<string, object?>
        {
            ["dependsOn"] = new List<object?> { $"${{{Id}AcrPull}}" },
        };

        if (job is not null)
        {
            // Container Apps job (upstream PublishAsAzureContainerAppJob / PublishAsScheduledAzureContainerAppJob).
            var configuration = new Dictionary<string, object?>
            {
                ["triggerType"] = job.CronExpression is null ? "Manual" : "Schedule",
                ["replicaTimeout"] = 1800,
                ["registries"] = registries,
            };
            if (job.CronExpression is not null)
            {
                configuration["scheduleTriggerConfig"] = new Dictionary<string, object?>
                {
                    ["cronExpression"] = job.CronExpression,
                    ["parallelism"] = 1,
                    ["replicaCompletionCount"] = 1,
                };
            }
            else
            {
                configuration["manualTriggerConfig"] = new Dictionary<string, object?> { ["parallelism"] = 1, ["replicaCompletionCount"] = 1 };
            }

            var jobResource = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:app:Job",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["jobName"] = appName,
                    ["resourceGroupName"] = rg,
                    ["environmentId"] = $"${{{Id}.id}}",
                    ["identity"] = identity,
                    ["configuration"] = configuration,
                    ["template"] = new Dictionary<string, object?> { ["containers"] = new List<object?> { container } },
                },
                ["options"] = options,
            };
            if (existingEnvironment) ((Dictionary<string, object?>)jobResource["properties"]!)["location"] = $"${{{Id}.location}}";
            return jobResource;
        }

        var appConfiguration = new Dictionary<string, object?>
        {
            ["activeRevisionsMode"] = "Single",
            ["registries"] = registries,
        };

        if (httpEndpoints.Count > 0)
        {
            appConfiguration["ingress"] = new Dictionary<string, object?>
            {
                ["external"] = httpEndpoints.Any(e => e.IsExternal),
                ["targetPort"] = targetPort,
                ["transport"] = "auto",
                ["allowInsecure"] = false,
            };
        }

        var app = new Dictionary<string, object?>
        {
            ["type"] = "azure-native:app:ContainerApp",
            ["properties"] = new Dictionary<string, object?>
            {
                ["containerAppName"] = appName,
                ["resourceGroupName"] = rg,
                ["managedEnvironmentId"] = $"${{{Id}.id}}",
                ["identity"] = identity,
                ["configuration"] = appConfiguration,
                ["template"] = new Dictionary<string, object?>
                {
                    ["containers"] = new List<object?> { container },
                    ["scale"] = new Dictionary<string, object?> { ["minReplicas"] = 1, ["maxReplicas"] = 1 },
                },
            },
            ["options"] = options,
        };

        // An app must live in its environment's region, which may differ from the stack's.
        if (existingEnvironment) ((Dictionary<string, object?>)app["properties"]!)["location"] = $"${{{Id}.location}}";

        // Integrations (e.g. Azure Functions: kind=functionapp) tailor the generated container app.
        foreach (var customize in r.Annotations.OfType<PulumiContainerAppCustomizationAnnotation>())
            customize.Configure(app);
        return app;
    }
}

/// <summary>Marks a compute resource to be deployed as a Container Apps job (see <c>PublishAsPulumiContainerAppJob</c>).</summary>
/// <param name="CronExpression">Cron schedule for a scheduled job; null for a manually-triggered job.</param>
public sealed record PulumiContainerAppJobAnnotation(string? CronExpression) : IResourceAnnotation;

/// <summary>Deployment target that represents the container app (or job) Pulumi creates for a compute resource.</summary>
public sealed class PulumiContainerAppResource(string name, IResource targetResource) : Resource(name)
{
    public IResource TargetResource { get; } = targetResource;
}
