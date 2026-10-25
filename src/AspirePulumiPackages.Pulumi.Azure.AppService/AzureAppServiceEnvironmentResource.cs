using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using AspirePulumiPackages.Pulumi.Azure.Base;
using Microsoft.Extensions.Logging;

namespace AspirePulumiPackages.Pulumi.Azure.AppService;

/// <summary>
/// An Azure App Service environment provisioned by Pulumi (azure-native). Pulumi counterpart of the official
/// <c>AzureAppServiceEnvironmentResource</c>: resource group, Linux App Service plan, container registry, pull identity
/// (AcrPull) and a "website contributor" identity (Reader), optionally the Aspire dashboard site and Application Insights
/// (backed by a Log Analytics workspace). Compute resources targeting it are built, pushed to the registry and deployed as
/// Linux container web apps.
/// </summary>
public sealed class AzureAppServiceEnvironmentResource : PulumiAzureResource, IComputeEnvironmentResource, IContainerRegistry, IPulumiManagedIdentityResource
{
    private const string AcrLoginTag = "acr-login";
    internal const string ContributorRoleSuffix = "Reader";

    public AzureAppServiceEnvironmentResource(string name) : base(name)
    {
        Annotations.Add(new PipelineStepAnnotation(_ => (IEnumerable<PipelineStep>)
        [
            new PipelineStep
            {
                Name = $"pulumi-prepare-{name}",
                Description = $"Targets compute resources at the App Service environment '{name}'",
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

    /// <summary>Whether to deploy the Aspire dashboard as a site in the environment (official default: true).</summary>
    public bool EnableDashboard { get; set; } = true;

    /// <summary>Whether to create an Application Insights component (and Log Analytics workspace) and wire apps to it.</summary>
    public bool EnableApplicationInsights { get; set; }

    /// <summary>App Service plan SKU name (official default: P0V3).</summary>
    public string SkuName { get; set; } = "P0V3";

    /// <summary>App Service plan SKU tier (official default: Premium).</summary>
    public string SkuTier { get; set; } = "Premium";

    private PulumiStackResource? _resolvedStack;

    private string Identity => $"{Id}Identity";
    private string ContributorIdentity => $"{Id}ContributorIdentity";

    /// <summary>The shared pull identity; every web app uses it and is granted data-plane roles through it.</summary>
    public string PrincipalIdExpression => $"${{{Identity}.principalId}}";

    public PulumiOutputReference NameOutputReference => OutputRef("RegistryName");
    public PulumiOutputReference RegistryEndpoint => OutputRef("RegistryLoginServer");
    public PulumiOutputReference PlanId => OutputRef("PlanId");
    public PulumiOutputReference DashboardUri => OutputRef("DashboardUri");

    ReferenceExpression IContainerRegistry.Name => ReferenceExpression.Create($"{NameOutputReference}");
    ReferenceExpression IContainerRegistry.Endpoint => ReferenceExpression.Create($"{RegistryEndpoint}");
    ReferenceExpression? IContainerRegistry.Repository => null;

    public ReferenceExpression GetHostAddressExpression(EndpointReference endpointReference) =>
        ReferenceExpression.Create($"{endpointReference.Resource.Name.ToLowerInvariant()}");

    public ReferenceExpression GetEndpointPropertyExpression(EndpointReferenceExpression endpointReferenceExpression) =>
        ReferenceExpression.Create($"{endpointReferenceExpression.Endpoint.Resource.Name.ToLowerInvariant()}");

    private PulumiOutputReference OutputRef(string suffix) =>
        Output(_resolvedStack ?? Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet."), suffix);

    private bool IsTargeted(IResource r) =>
        r.Annotations.OfType<DeploymentTargetAnnotation>().Any(a => a.ComputeEnvironment == this);

    private async Task LoginToRegistryAsync(PipelineStepContext ctx)
    {
        var registry = await OutputRef("RegistryName").GetValueAsync(ctx.CancellationToken);
        var subscription = await OutputRef("SubscriptionId").GetValueAsync(ctx.CancellationToken);

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

            r.Annotations.Add(new DeploymentTargetAnnotation(new PulumiWebSiteResource(r.Name, r))
            {
                ContainerRegistry = this,
                ComputeEnvironment = this,
            });

            r.Annotations.Add(new ContainerImagePushOptionsCallbackAnnotation(c =>
            {
                c.Options.RemoteImageName = r.Name.ToLowerInvariant();
                c.Options.RemoteImageTag = _resolvedStack.DeployTag;
            }));
        }

        return Task.CompletedTask;
    }

    protected override async Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string resourceGroup, string location)
    {
        var stack = _resolvedStack ?? ResolveStack(context);
        var tags = new Dictionary<string, object?> { ["aspire-resource-name"] = Name };
        program.AddClientConfig();

        // Container registry (the official integration creates a "<name>-acr" registry; Basic SKU).
        program.AddInfraResource($"{Id}Registry", "azure-native:containerregistry:Registry", new()
        {
            ["type"] = "azure-native:containerregistry:Registry",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = resourceGroup,
                ["location"] = location,
                ["sku"] = new Dictionary<string, object?> { ["name"] = "Basic" },
                ["tags"] = tags,
            },
        });

        // Pull identity + AcrPull on the registry.
        program.AddUserAssignedIdentity(Identity, resourceGroup, location);
        var acrPull = program.AddRoleAssignment($"{Id}AcrPull", PrincipalIdExpression, AzureRoleDefinitions.AcrPull, $"${{{Id}Registry.id}}");

        // Linux App Service plan.
        program.AddInfraResource($"{Id}Plan", "azure-native:web:AppServicePlan", new()
        {
            ["type"] = "azure-native:web:AppServicePlan",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = resourceGroup,
                ["location"] = location,
                ["kind"] = "Linux",
                ["reserved"] = true,
                ["perSiteScaling"] = true,
                ["sku"] = new Dictionary<string, object?> { ["name"] = SkuName, ["tier"] = SkuTier },
            },
        });

        // Website contributor identity (Reader on the resource group), used by the dashboard to discover the environment.
        program.AddUserAssignedIdentity(ContributorIdentity, resourceGroup, location);
        program.AddRoleAssignment($"{Id}ContributorReader", $"${{{ContributorIdentity}.principalId}}", AzureRoleDefinitions.Reader, $"${{{Id}Rg.id}}");

        if (EnableApplicationInsights)
        {
            program.AddInfraResource($"{Id}Logs", "azure-native:operationalinsights:Workspace", new()
            {
                ["type"] = "azure-native:operationalinsights:Workspace",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = resourceGroup,
                    ["location"] = location,
                    ["retentionInDays"] = 30,
                    ["sku"] = new Dictionary<string, object?> { ["name"] = "PerGB2018" },
                    ["tags"] = tags,
                },
            });
            program.AddInfraResource($"{Id}AppInsights", "azure-native:applicationinsights:Component", new()
            {
                ["type"] = "azure-native:applicationinsights:Component",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = resourceGroup,
                    ["location"] = location,
                    ["applicationType"] = "web",
                    ["kind"] = "web",
                    ["workspaceResourceId"] = $"${{{Id}Logs.id}}",
                    ["tags"] = tags,
                },
            });
            program.Outputs[$"{Id}LogAnalyticsWorkspaceId"] = $"${{{Id}Logs.id}}";
            program.Outputs[$"{Id}AppInsightsConnectionString"] = new Dictionary<string, object?>
            {
                ["fn::secret"] = $"${{{Id}AppInsights.connectionString}}",
            };
        }

        if (EnableDashboard)
        {
            program.AddInfraResource($"{Id}Dashboard", "azure-native:web:WebApp", new()
            {
                ["type"] = "azure-native:web:WebApp",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = resourceGroup,
                    ["location"] = location,
                    ["kind"] = "app,linux,aspiredashboard",
                    ["serverFarmId"] = $"${{{Id}Plan.id}}",
                    ["httpsOnly"] = true,
                    ["identity"] = UserAssigned(ContributorIdentity),
                    ["siteConfig"] = new Dictionary<string, object?>
                    {
                        ["numberOfWorkers"] = 1,
                        ["linuxFxVersion"] = "ASPIREDASHBOARD|1.0",
                        ["acrUseManagedIdentityCreds"] = true,
                        ["acrUserManagedIdentityID"] = $"${{{Identity}.clientId}}",
                        ["alwaysOn"] = true,
                        ["http20Enabled"] = true,
                        ["http20ProxyFlag"] = 1,
                        ["appSettings"] = Settings(
                            ("DASHBOARD__FRONTEND__AUTHMODE", "Unsecured"),
                            ("DASHBOARD__OTLP__AUTHMODE", "Unsecured"),
                            ("DASHBOARD__OTLP__SUPPRESSUNSECUREDTELEMETRYMESSAGE", "true"),
                            ("DASHBOARD__RESOURCESERVICECLIENT__AUTHMODE", "Unsecured"),
                            ("DASHBOARD__UI__DISABLEIMPORT", "true"),
                            ("WEBSITES_PORT", "5000"),
                            ("HTTP20_ONLY_PORT", "4317"),
                            ("WEBSITE_START_SCM_WITH_PRELOAD", "true"),
                            ("AZURE_CLIENT_ID", $"${{{ContributorIdentity}.clientId}}"),
                            ("ALLOWED_MANAGED_IDENTITIES", $"${{{Identity}.clientId}}"),
                            ("ASPIRE_ENVIRONMENT_NAME", Name)),
                    },
                    ["tags"] = tags,
                },
            });
            program.Outputs[$"{Id}DashboardUri"] = $"https://${{{Id}Dashboard.defaultHostName}}";
        }

        program.Outputs[$"{Id}PlanName"] = $"${{{Id}Plan.name}}";
        program.Outputs[$"{Id}PlanId"] = $"${{{Id}Plan.id}}";
        program.Outputs[$"{Id}ResourceGroup"] = resourceGroup;
        program.Outputs[$"{Id}SubscriptionId"] = AzureSubscription;
        program.Outputs[$"{Id}RegistryName"] = $"${{{Id}Registry.name}}";
        program.Outputs[$"{Id}RegistryLoginServer"] = $"${{{Id}Registry.loginServer}}";
        program.Outputs[$"{Id}RegistryManagedIdentityId"] = $"${{{Identity}.id}}";
        program.Outputs[$"{Id}RegistryManagedIdentityClientId"] = $"${{{Identity}.clientId}}";
        program.Outputs[$"{Id}WebsiteContributorManagedIdentityId"] = $"${{{ContributorIdentity}.id}}";
        program.Outputs[$"{Id}WebsiteContributorManagedIdentityPrincipalId"] = $"${{{ContributorIdentity}.principalId}}";

        foreach (var r in context.Model.GetComputeResources().Where(IsTargeted))
        {
            program.HasApps = true;
            program.Resources[AppId(r)] = await BuildWebAppAsync(r, stack, context, resourceGroup, location, acrPull, tags);
        }
    }

    private const string AzureSubscription = PulumiAzureProgramExtensions.SubscriptionId;

    private string AppId(IResource r) => $"{PulumiAzureProgramExtensions.ToPulumiId(r.Name)}Site";

    private static Dictionary<string, object?> UserAssigned(string identityId) => new()
    {
        ["type"] = "UserAssigned",
        ["userAssignedIdentities"] = new List<object?> { $"${{{identityId}.id}}" },
    };

    private static List<object?> Settings(params (string Name, string Value)[] settings) =>
        settings.Select(s => (object?)new Dictionary<string, object?> { ["name"] = s.Name, ["value"] = s.Value }).ToList();

    private async Task<Dictionary<string, object?>> BuildWebAppAsync(IResource r, PulumiStackResource stack, PipelineStepContext ctx,
        string resourceGroup, string location, string acrPull, Dictionary<string, object?> tags)
    {
        var appName = r.Name.ToLowerInvariant();

        var envVars = new Dictionary<string, object>();
        var envContext = new EnvironmentCallbackContext(ctx.ExecutionContext, r, envVars, ctx.CancellationToken);
        foreach (var a in r.Annotations.OfType<EnvironmentCallbackAnnotation>())
            await a.Callback(envContext);

        var httpEndpoints = r.TryGetEndpoints(out var eps)
            ? eps.Where(e => e.UriScheme is "http" or "https").ToList()
            : [];
        var targetPort = httpEndpoints.FirstOrDefault()?.TargetPort ?? 8080;

        var settings = new List<object?>();
        void Add(string name, string value) =>
            settings.Add(new Dictionary<string, object?> { ["name"] = name, ["value"] = value });

        foreach (var (key, value) in envVars)
        {
            // Endpoint-reference ports can't be resolved at publish time; the site listens on WEBSITES_PORT.
            if (key is "HTTP_PORTS")
            {
                Add(key, targetPort.ToString());
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

            if (resolved is not null) Add(key, resolved);
        }

        Add("WEBSITES_PORT", targetPort.ToString());
        Add("AZURE_CLIENT_ID", $"${{{Identity}.clientId}}");
        if (EnableApplicationInsights)
            Add("APPLICATIONINSIGHTS_CONNECTION_STRING", $"${{{Id}AppInsights.connectionString}}");
        if (EnableDashboard)
        {
            Add("OTEL_SERVICE_NAME", appName);
            Add("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc");
            Add("OTEL_EXPORTER_OTLP_ENDPOINT", $"https://${{{Id}Dashboard.defaultHostName}}:443");
        }

        return new()
        {
            ["type"] = "azure-native:web:WebApp",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = resourceGroup,
                ["location"] = location,
                ["kind"] = "app,linux,container",
                ["serverFarmId"] = $"${{{Id}Plan.id}}",
                ["httpsOnly"] = true,
                ["identity"] = UserAssigned(Identity),
                ["siteConfig"] = new Dictionary<string, object?>
                {
                    ["numberOfWorkers"] = 1,
                    ["linuxFxVersion"] = $"DOCKER|${{{Id}Registry.loginServer}}/{appName}:{stack.DeployTag}",
                    ["acrUseManagedIdentityCreds"] = true,
                    ["acrUserManagedIdentityID"] = $"${{{Identity}.clientId}}",
                    ["alwaysOn"] = true,
                    ["http20Enabled"] = true,
                    ["appSettings"] = settings,
                },
                ["tags"] = new Dictionary<string, object?>(tags) { ["aspire-resource-name"] = r.Name },
            },
            ["options"] = new Dictionary<string, object?>
            {
                ["dependsOn"] = new List<object?> { $"${{{acrPull}}}" },
            },
        };
    }
}

/// <summary>Deployment target that represents the web app Pulumi creates for a compute resource.</summary>
public sealed class PulumiWebSiteResource(string name, IResource targetResource) : Resource(name)
{
    public IResource TargetResource { get; } = targetResource;
}
