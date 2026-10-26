using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using AspirePulumiPackages.Pulumi.Azure.Base;
using PulumiExpr = AspirePulumiPackages.Pulumi.Provisioning.PulumiExpr;

namespace AspirePulumiPackages.Pulumi.Azure.Foundry;

/// <summary>
/// A Microsoft Foundry account provisioned by Pulumi (azure-native), mirroring Aspire.Hosting.Foundry's Bicep: a
/// <c>Microsoft.CognitiveServices/accounts</c> of kind AIServices (S0, system-assigned identity, local auth disabled, public
/// network access enabled, project management allowed), an Agents capability host, the model deployments (created one after
/// another) and the projects added to the builder. Compute environments hosting an app that references the account, one of its
/// deployments or one of its projects are granted Cognitive Services User and Cognitive Services OpenAI User by default
/// (override with WithRoleAssignments).
/// </summary>
public sealed class FoundryResource(string name) : PulumiAzureResource(name), IResourceWithConnectionString
{
    internal static readonly FoundryRole[] DefaultRoles = [FoundryRole.CognitiveServicesUser, FoundryRole.CognitiveServicesOpenAIUser];
    private const string SubscriptionId = PulumiAzureProgramExtensions.SubscriptionId;

    /// <summary>Account SKU (default S0, as in the official integration).</summary>
    public string Sku { get; set; } = "S0";

    /// <summary>
    /// Custom sub-domain of the account endpoint (globally unique). When null, a name is derived from the resource name plus the
    /// first segment of the subscription id (the official Bicep uses <c>uniqueString(resourceGroup().id)</c>).
    /// </summary>
    public string? CustomSubDomainName { get; set; }

    public List<FoundryDeploymentResource> Deployments { get; } = [];
    public List<FoundryProjectResource> Projects { get; } = [];

    internal PulumiOutputReference OutputRef(string suffix) =>
        Output(Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack()."), suffix);

    /// <summary>The account endpoint, e.g. <c>https://myfoundry.cognitiveservices.azure.com/</c>.</summary>
    public PulumiOutputReference Endpoint => OutputRef("Endpoint");

    /// <summary>The AI Foundry API endpoint, e.g. <c>https://myfoundry.services.ai.azure.com/</c>.</summary>
    public PulumiOutputReference AIFoundryApiEndpoint => OutputRef("AiFoundryApiEndpoint");
    public PulumiOutputReference NameOutput => OutputRef("Name");

    /// <summary>The connection string, <c>Endpoint=&lt;endpoint&gt;;EndpointAIInference=&lt;AI Foundry API endpoint&gt;models</c>.</summary>
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"Endpoint={Endpoint};EndpointAIInference={AIFoundryApiEndpoint}models");

    /// <summary>The service endpoint URI expression (the AI Foundry API endpoint).</summary>
    public ReferenceExpression UriExpression => ReferenceExpression.Create($"{AIFoundryApiEndpoint}");

    private object SubDomain()
    {
        if (CustomSubDomainName is { } custom) return custom;
        var prefix = new string(Name.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).Take(15).ToArray());
        if (prefix.Length == 0) prefix = "foundry";
        return PulumiExpr.Join("", prefix, PulumiExpr.Select(0, PulumiExpr.Split("-", SubscriptionId)));
    }

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        program.AddClientConfig();

        // No accountName: the provider auto-names it, the equivalent of Bicep's toLower('<name>-${uniqueString(...)}').
        // An infra target: role assignments are created in the targeted provision pass and depend on the account.
        program.AddInfraResource(Id, "azure-native:cognitiveservices:Account", new Dictionary<string, object?>
        {
            ["type"] = "azure-native:cognitiveservices:Account",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = rg,
                ["location"] = location,
                ["kind"] = "AIServices",
                ["identity"] = new Dictionary<string, object?> { ["type"] = "SystemAssigned" },
                ["sku"] = new Dictionary<string, object?> { ["name"] = Sku },
                ["properties"] = new Dictionary<string, object?>
                {
                    ["customSubDomainName"] = SubDomain(),
                    ["publicNetworkAccess"] = "Enabled",
                    ["disableLocalAuth"] = true,
                    ["allowProjectManagement"] = true,
                },
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
            },
        });

        // The Bicep also sets enablePublicHostingEnvironment (API 2025-10-01-preview), which azure-native's default
        // CapabilityHost type does not expose; only the kind is set here.
        program.Resources[$"{Id}Caphost"] = new Dictionary<string, object?>
        {
            ["type"] = "azure-native:cognitiveservices:AccountCapabilityHost",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = rg,
                ["accountName"] = $"${{{Id}.name}}",
                ["capabilityHostName"] = $"{Name}-caphost",
                ["capabilityHostProperties"] = new Dictionary<string, object?> { ["capabilityHostKind"] = "Agents" },
            },
        };

        string? previous = null;
        foreach (var d in Deployments)
        {
            var id = $"{Id}{d.Id}";
            var def = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:cognitiveservices:Deployment",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["accountName"] = $"${{{Id}.name}}",
                    ["deploymentName"] = d.DeploymentName,
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["model"] = new Dictionary<string, object?>
                        {
                            ["format"] = d.Format,
                            ["name"] = d.ModelName,
                            ["version"] = d.ModelVersion,
                        },
                    },
                    ["sku"] = new Dictionary<string, object?> { ["name"] = d.SkuName, ["capacity"] = d.SkuCapacity },
                },
            };
            // Azure rejects concurrent deployment writes on one account, so chain them.
            if (previous is not null)
                def["options"] = new Dictionary<string, object?> { ["dependsOn"] = new List<object?> { $"${{{previous}}}" } };
            program.Resources[id] = def;
            previous = id;
        }

        foreach (var p in Projects) AddProject(program, p, rg, location);

        program.Outputs[$"{Id}AiFoundryApiEndpoint"] = $"${{{Id}.properties.endpoints[\"AI Foundry API\"]}}";
        program.Outputs[$"{Id}Endpoint"] = $"${{{Id}.properties.endpoint}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";

        AddRoleAssignments(program, context);
        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }

    /// <summary>
    /// A project (system-assigned identity) with an Application Insights component the project identity may publish metrics to,
    /// connected to the project through an ApiKey connection (category AppInsights), as the official Bicep does.
    /// </summary>
    private void AddProject(PulumiProgram program, FoundryProjectResource p, string rg, string location)
    {
        var id = $"{Id}{p.Id}";
        program.Resources[id] = new Dictionary<string, object?>
        {
            ["type"] = "azure-native:cognitiveservices:Project",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = rg,
                ["accountName"] = $"${{{Id}.name}}",
                ["projectName"] = p.Name,
                ["location"] = location,
                ["identity"] = new Dictionary<string, object?> { ["type"] = "SystemAssigned" },
                ["properties"] = new Dictionary<string, object?> { ["displayName"] = p.Name },
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = p.Name },
            },
        };

        var ai = $"{id}Ai";
        program.Resources[ai] = new Dictionary<string, object?>
        {
            ["type"] = "azure-native:applicationinsights:Component",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = rg,
                ["location"] = location,
                ["kind"] = "web",
                ["applicationType"] = "web",
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = p.Name },
            },
        };

        // Monitoring Metrics Publisher for the project identity on the Application Insights component.
        program.AddRoleAssignment($"{ai}MonitoringMetricsPublisher", $"${{{id}.identity.principalId}}",
            "3913510d-42f4-4e42-8a64-420c390055eb", $"${{{ai}.id}}");

        program.Resources[$"{ai}Conn"] = new Dictionary<string, object?>
        {
            ["type"] = "azure-native:cognitiveservices:ProjectConnection",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = rg,
                ["accountName"] = $"${{{Id}.name}}",
                ["projectName"] = $"${{{id}.name}}",
                ["connectionName"] = $"{p.Name}-ai-conn",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["authType"] = "ApiKey",
                    ["category"] = "AppInsights",
                    ["isSharedToAll"] = false,
                    ["target"] = $"${{{ai}.id}}",
                    ["metadata"] = new Dictionary<string, object?>
                    {
                        ["ApiType"] = "Azure",
                        ["ResourceId"] = $"${{{ai}.id}}",
                        ["location"] = $"${{{ai}.location}}",
                    },
                    ["credentials"] = new Dictionary<string, object?> { ["key"] = $"${{{ai}.connectionString}}" },
                },
            },
        };

        program.Outputs[$"{id}Id"] = $"${{{id}.id}}";
        program.Outputs[$"{id}Name"] = $"${{{Id}.name}}/{p.Name}";
        program.Outputs[$"{id}Endpoint"] = $"${{{id}.properties.endpoints[\"AI Foundry API\"]}}";
        program.Outputs[$"{id}PrincipalId"] = $"${{{id}.identity.principalId}}";
        program.Outputs[$"{id}AppInsightsConnectionString"] = $"${{{ai}.connectionString}}";
    }

    private bool IsReferencedBy(IResource consumer)
    {
        var referenced = consumer.Annotations.OfType<ResourceRelationshipAnnotation>().Select(a => a.Resource).ToList();
        return referenced.Contains(this) || Deployments.Any(referenced.Contains) || Projects.Any(referenced.Contains);
    }

    /// <summary>Grants each compute environment hosting a referencing app its roles on the account.</summary>
    private void AddRoleAssignments(PulumiProgram program, PipelineStepContext ctx)
    {
        var grants = new Dictionary<IPulumiManagedIdentityResource, HashSet<FoundryRole>>();
        foreach (var consumer in ctx.Model.GetComputeResources().Where(IsReferencedBy))
        {
            var custom = consumer.Annotations.OfType<FoundryRoleAnnotation>().Where(a => a.Target == this).ToList();
            IEnumerable<FoundryRole> roles = custom.Count > 0 ? custom.SelectMany(a => a.Roles) : DefaultRoles;
            foreach (var env in consumer.Annotations.OfType<DeploymentTargetAnnotation>()
                         .Select(a => a.ComputeEnvironment).OfType<IPulumiManagedIdentityResource>())
            {
                if (!grants.TryGetValue(env, out var set)) grants[env] = set = [];
                set.UnionWith(roles);
            }
        }

        foreach (var (env, roles) in grants)
        {
            var envId = PulumiAzureProgramExtensions.ToPulumiId(env.Name);
            foreach (var role in roles)
                program.AddRoleAssignment($"{Id}{envId}{role}", env.PrincipalIdExpression,
                    FoundryRoles.RoleDefinitionId(role), $"${{{Id}.id}}");
        }
    }
}

/// <summary>A model deployment of a <see cref="FoundryResource"/> (connection string = account connection string + <c>;Deployment=</c>).</summary>
public sealed class FoundryDeploymentResource : Resource, IResourceWithConnectionString, IResourceWithParent<FoundryResource>
{
    private string _deploymentName;
    private string _modelName;
    private string _modelVersion;
    private string _format;
    private string _skuName = "GlobalStandard";

    public FoundryDeploymentResource(string name, string modelName, string modelVersion, string format, FoundryResource parent) : base(name)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelName);
        ArgumentException.ThrowIfNullOrEmpty(modelVersion);
        ArgumentException.ThrowIfNullOrEmpty(format);
        ArgumentNullException.ThrowIfNull(parent);
        _deploymentName = name;
        _modelName = modelName;
        _modelVersion = modelVersion;
        _format = format;
        Parent = parent;
    }

    public FoundryResource Parent { get; }

    /// <summary>The deployment name in Azure (defaults to the resource name).</summary>
    public string DeploymentName { get => _deploymentName; set { ArgumentException.ThrowIfNullOrEmpty(value); _deploymentName = value; } }
    public string ModelName { get => _modelName; set { ArgumentException.ThrowIfNullOrEmpty(value); _modelName = value; } }
    public string ModelVersion { get => _modelVersion; set { ArgumentException.ThrowIfNullOrEmpty(value); _modelVersion = value; } }
    /// <summary>The model format / publisher, e.g. <c>OpenAI</c>.</summary>
    public string Format { get => _format; set { ArgumentException.ThrowIfNullOrEmpty(value); _format = value; } }
    /// <summary>Deployment SKU (default GlobalStandard).</summary>
    public string SkuName { get => _skuName; set { ArgumentException.ThrowIfNullOrEmpty(value); _skuName = value; } }
    /// <summary>Deployment SKU capacity (default 1).</summary>
    public int SkuCapacity { get; set; } = 1;

    internal string Id => PulumiAzureProgramExtensions.ToPulumiId(Name);

    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"{Parent.ConnectionStringExpression};Deployment={DeploymentName}");
}

/// <summary>A Foundry project of a <see cref="FoundryResource"/> (connection string = <c>Endpoint=&lt;project endpoint&gt;</c>).</summary>
public sealed class FoundryProjectResource(string name, FoundryResource parent)
    : Resource(name), IResourceWithConnectionString, IResourceWithParent<FoundryResource>
{
    public FoundryResource Parent { get; } = parent;

    internal string Id => PulumiAzureProgramExtensions.ToPulumiId(Name);

    private PulumiOutputReference OutputRef(string suffix) => Parent.OutputRef($"{Id}{suffix}");

    /// <summary>The project API endpoint.</summary>
    public PulumiOutputReference Endpoint => OutputRef("Endpoint");

    /// <summary>The project's system-assigned identity principal id.</summary>
    public PulumiOutputReference PrincipalId => OutputRef("PrincipalId");

    /// <summary>The Application Insights connection string created for the project.</summary>
    public PulumiOutputReference AppInsightsConnectionString => OutputRef("AppInsightsConnectionString");

    public ReferenceExpression ConnectionStringExpression => ReferenceExpression.Create($"Endpoint={Endpoint}");

    public ReferenceExpression UriExpression => ReferenceExpression.Create($"{Endpoint}");
}

/// <summary>Roles the annotated resource's identity should get on an account (replaces the default).</summary>
internal sealed class FoundryRoleAnnotation(FoundryResource target, FoundryRole[] roles) : IResourceAnnotation
{
    public FoundryResource Target { get; } = target;
    public FoundryRole[] Roles { get; } = roles;
}
