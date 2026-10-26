using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using AspirePulumiPackages.Pulumi.Azure.Base;
using PulumiExpr = AspirePulumiPackages.Pulumi.Provisioning.PulumiExpr;

namespace AspirePulumiPackages.Pulumi.Azure.CognitiveServices;

/// <summary>
/// An Azure OpenAI (Cognitive Services, kind OpenAI) account provisioned by Pulumi (azure-native), mirroring
/// Aspire.Hosting.Azure.CognitiveServices's Bicep: an S0 account with local (key) auth disabled and public network access
/// enabled, plus the model deployments added to the builder (created one after another, as in the official Bicep).
/// Compute environments hosting an app that references the account (or one of its deployments) are granted
/// Cognitive Services OpenAI User by default (override with WithRoleAssignments).
/// </summary>
public sealed class AzureOpenAIResource(string name) : PulumiAzureResource(name), IResourceWithConnectionString
{
    internal static readonly AzureOpenAIRole[] DefaultRoles = [AzureOpenAIRole.CognitiveServicesOpenAIUser];

    /// <summary>Account SKU (default S0, as in the official integration).</summary>
    public string Sku { get; set; } = "S0";

    /// <summary>
    /// Custom sub-domain of the account endpoint (required for Entra ID auth; globally unique). When null, a name is derived from
    /// the resource name plus the first segment of the subscription id (the official Bicep uses <c>uniqueString(resourceGroup().id)</c>).
    /// </summary>
    public string? CustomSubDomainName { get; set; }

    public List<AzureOpenAIDeploymentResource> Deployments { get; } = [];

    internal PulumiOutputReference OutputRef(string suffix) =>
        Output(Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack()."), suffix);

    /// <summary>The account endpoint, e.g. <c>https://myaccount.openai.azure.com/</c>.</summary>
    public PulumiOutputReference Endpoint => OutputRef("Endpoint");
    public PulumiOutputReference NameOutput => OutputRef("Name");

    /// <summary>The connection string, <c>Endpoint=&lt;endpoint&gt;</c>.</summary>
    public ReferenceExpression ConnectionStringExpression => ReferenceExpression.Create($"Endpoint={Endpoint}");

    /// <summary>The service endpoint URI expression.</summary>
    public ReferenceExpression UriExpression => ReferenceExpression.Create($"{Endpoint}");

    internal ReferenceExpression GetConnectionString(string deploymentName) =>
        ReferenceExpression.Create($"{ConnectionStringExpression};Deployment={deploymentName}");

    private object SubDomain()
    {
        if (CustomSubDomainName is { } custom) return custom;
        var prefix = new string(Name.ToLowerInvariant().Where(c => char.IsAsciiLetterOrDigit(c)).Take(15).ToArray());
        if (prefix.Length == 0) prefix = "openai";
        return PulumiExpr.Join("", prefix, PulumiExpr.Select(0, PulumiExpr.Split("-", PulumiProgramSubscriptionId)));
    }

    private const string PulumiProgramSubscriptionId = PulumiAzureProgramExtensions.SubscriptionId;

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        program.AddClientConfig();

        // No accountName: the provider auto-names it (<id> + random suffix), the equivalent of Bicep's
        // take('<name>-${uniqueString(resourceGroup().id)}', 64).
        // An infra target: role assignments are created in the targeted provision pass and depend on the account.
        program.AddInfraResource(Id, "azure-native:cognitiveservices:Account", new Dictionary<string, object?>
        {
            ["type"] = "azure-native:cognitiveservices:Account",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = rg,
                ["location"] = location,
                ["kind"] = "OpenAI",
                ["sku"] = new Dictionary<string, object?> { ["name"] = Sku },
                ["properties"] = new Dictionary<string, object?>
                {
                    ["customSubDomainName"] = SubDomain(),
                    ["publicNetworkAccess"] = "Enabled",
                    ["disableLocalAuth"] = true,
                },
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
            },
        });

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
                            ["format"] = "OpenAI",
                            ["name"] = d.ModelName,
                            ["version"] = d.ModelVersion,
                        },
                    },
                    ["sku"] = new Dictionary<string, object?> { ["name"] = d.SkuName, ["capacity"] = d.SkuCapacity },
                },
            };
            // Azure rejects concurrent deployment writes on one account, so chain them (the official Bicep does the same).
            if (previous is not null)
                def["options"] = new Dictionary<string, object?> { ["dependsOn"] = new List<object?> { $"${{{previous}}}" } };
            program.Resources[id] = def;
            previous = id;
        }

        program.Outputs[$"{Id}Endpoint"] = $"${{{Id}.properties.endpoint}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";

        AddRoleAssignments(program, context);
        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }

    private bool IsReferencedBy(IResource consumer)
    {
        var referenced = consumer.Annotations.OfType<ResourceRelationshipAnnotation>().Select(a => a.Resource).ToList();
        return referenced.Contains(this) || Deployments.Any(referenced.Contains);
    }

    /// <summary>Grants each compute environment hosting a referencing app its roles on the account.</summary>
    private void AddRoleAssignments(PulumiProgram program, PipelineStepContext ctx)
    {
        var grants = new Dictionary<IPulumiManagedIdentityResource, HashSet<AzureOpenAIRole>>();
        foreach (var consumer in ctx.Model.GetComputeResources().Where(IsReferencedBy))
        {
            var custom = consumer.Annotations.OfType<AzureOpenAIRoleAnnotation>().Where(a => a.Target == this).ToList();
            IEnumerable<AzureOpenAIRole> roles = custom.Count > 0 ? custom.SelectMany(a => a.Roles) : DefaultRoles;
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
                    AzureOpenAIRoles.RoleDefinitionId(role), $"${{{Id}.id}}");
        }
    }
}

/// <summary>A model deployment of an <see cref="AzureOpenAIResource"/> (connection string = account connection string + <c>;Deployment=</c>).</summary>
public sealed class AzureOpenAIDeploymentResource : Resource, IResourceWithConnectionString, IResourceWithParent<AzureOpenAIResource>
{
    private string _deploymentName;
    private string _modelName;
    private string _modelVersion;
    private string _skuName = "Standard";

    public AzureOpenAIDeploymentResource(string name, string modelName, string modelVersion, AzureOpenAIResource parent) : base(name)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelName);
        ArgumentException.ThrowIfNullOrEmpty(modelVersion);
        ArgumentNullException.ThrowIfNull(parent);
        _deploymentName = name;
        _modelName = modelName;
        _modelVersion = modelVersion;
        Parent = parent;
    }

    public AzureOpenAIResource Parent { get; }

    /// <summary>The deployment name in Azure (defaults to the resource name).</summary>
    public string DeploymentName { get => _deploymentName; set { ArgumentException.ThrowIfNullOrEmpty(value); _deploymentName = value; } }
    public string ModelName { get => _modelName; set { ArgumentException.ThrowIfNullOrEmpty(value); _modelName = value; } }
    public string ModelVersion { get => _modelVersion; set { ArgumentException.ThrowIfNullOrEmpty(value); _modelVersion = value; } }
    /// <summary>Deployment SKU (default Standard).</summary>
    public string SkuName { get => _skuName; set { ArgumentException.ThrowIfNullOrEmpty(value); _skuName = value; } }
    /// <summary>Deployment SKU capacity (default 8).</summary>
    public int SkuCapacity { get; set; } = 8;

    internal string Id => PulumiAzureProgramExtensions.ToPulumiId(Name);

    public ReferenceExpression ConnectionStringExpression => Parent.GetConnectionString(DeploymentName);
}

/// <summary>Roles the annotated resource's identity should get on an account (replaces the default).</summary>
internal sealed class AzureOpenAIRoleAnnotation(AzureOpenAIResource target, AzureOpenAIRole[] roles) : IResourceAnnotation
{
    public AzureOpenAIResource Target { get; } = target;
    public AzureOpenAIRole[] Roles { get; } = roles;
}
