using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting.ApplicationModel;
using PulumiExpr = Sheridan.Aspire.Pulumi.Provisioning.PulumiExpr;
using Sheridan.Aspire.Pulumi.Azure.ApplicationInsights;
using Sheridan.Aspire.Pulumi.Azure.Base;
using Sheridan.Aspire.Pulumi.Azure.CosmosDB;
using Sheridan.Aspire.Pulumi.Azure.KeyVault;
using Sheridan.Aspire.Pulumi.Azure.Search;
using Sheridan.Aspire.Pulumi.Azure.Storage;

namespace Sheridan.Aspire.Pulumi.Azure.Foundry;

/// <summary>A Foundry project of a <see cref="FoundryResource"/> (connection string = <c>Endpoint=&lt;project endpoint&gt;</c>).</summary>
public sealed class FoundryProjectResource(string name, FoundryResource parent)
    : Resource(name), IResourceWithConnectionString, IResourceWithParent<FoundryResource>
{
    internal const string CapabilityHostIdSuffix = "ProjectCaphost";

    public FoundryResource Parent { get; } = parent;

    internal string Id => PulumiAzureProgramExtensions.ToPulumiId(Name);

    private PulumiOutputReference OutputRef(string suffix) => Parent.OutputRef($"{Id}{suffix}");

    /// <summary>
    /// The Application Insights resource telemetry is sent to (set by <c>WithAppInsights</c>); when null a component named after the
    /// project is created. It must be provisioned in the same Pulumi stack.
    /// </summary>
    public PulumiApplicationInsightsResource? AppInsights { get; set; }

    /// <summary>The Key Vault connection (set by <c>WithKeyVault</c>); all other connections depend on it.</summary>
    public FoundryProjectConnectionResource? KeyVaultConnection { get; internal set; }

    /// <summary>The connections added with <c>AddConnection</c> / <c>AddBingGroundingConnection</c> / <c>WithKeyVault</c>.</summary>
    public List<FoundryProjectConnectionResource> Connections { get; } = [];

    /// <summary>The project capability host (set by <c>AddCapabilityHost</c>).</summary>
    public FoundryCapabilityHostConfiguration? CapabilityHost { get; internal set; }

    /// <summary>The project API endpoint.</summary>
    public PulumiOutputReference Endpoint => OutputRef("Endpoint");

    /// <summary>The project's system-assigned identity principal id.</summary>
    public PulumiOutputReference PrincipalId => OutputRef("PrincipalId");

    /// <summary>The Application Insights connection string used by the project.</summary>
    public PulumiOutputReference AppInsightsConnectionString => OutputRef("AppInsightsConnectionString");

    public ReferenceExpression ConnectionStringExpression => ReferenceExpression.Create($"Endpoint={Endpoint}");

    public ReferenceExpression UriExpression => ReferenceExpression.Create($"{Endpoint}");
}

/// <summary>
/// A connection of a Foundry project to another resource (Storage, Cosmos DB, Search, Key Vault, Container Registry, Application
/// Insights, Bing grounding or a custom one). Pulumi counterpart of <c>AzureCognitiveServicesProjectConnectionResource</c>.
/// </summary>
public sealed class FoundryProjectConnectionResource : Resource, IResourceWithParent<FoundryProjectResource>
{
    internal FoundryProjectConnectionResource(string name, string connectionName, FoundryProjectResource parent,
        Func<FoundryConnectionContext, Dictionary<string, object?>> configure, Action<FoundryConnectionContext>? addRoleAssignments = null,
        bool accountLevel = false) : base(name)
    {
        ConnectionName = connectionName;
        Parent = parent;
        Configure = configure;
        AddRoleAssignments = addRoleAssignments;
        AccountLevel = accountLevel;
    }

    public FoundryProjectResource Parent { get; }

    /// <summary>The connection's name in Azure.</summary>
    public string ConnectionName { get; }

    /// <summary>True for a connection created on the account instead of the project (Bing grounding).</summary>
    public bool AccountLevel { get; }

    internal Func<FoundryConnectionContext, Dictionary<string, object?>> Configure { get; }
    internal Action<FoundryConnectionContext>? AddRoleAssignments { get; }

    internal string Id => PulumiAzureProgramExtensions.ToPulumiId(Name);

    private PulumiOutputReference OutputRef(string suffix) => Parent.Parent.OutputRef($"{Parent.Id}{Id}{suffix}");

    /// <summary>The connection name, as output by the stack.</summary>
    public PulumiOutputReference NameOutput => OutputRef("Name");

    /// <summary>The connection's ARM resource id, as output by the stack.</summary>
    public PulumiOutputReference IdOutput => OutputRef("Id");
}

/// <summary>What a connection needs while the Pulumi program is being built.</summary>
public sealed class FoundryConnectionContext(PulumiProgram program, FoundryResource account, FoundryProjectResource project, string projectId, string resourceGroup, string location)
{
    public PulumiProgram Program { get; } = program;
    public FoundryResource Account { get; } = account;
    public FoundryProjectResource Project { get; } = project;

    /// <summary>The Pulumi identifier of the project resource.</summary>
    public string ProjectId { get; } = projectId;

    public string ResourceGroup { get; } = resourceGroup;
    public string Location { get; } = location;

    /// <summary>Pulumi expression of the project's system-assigned identity principal id.</summary>
    public string ProjectPrincipalId => $"${{{ProjectId}.identity.principalId}}";

    /// <summary>The standard connection metadata (<c>ApiType</c>, <c>ResourceId</c>, <c>location</c>) for the Pulumi resource <paramref name="targetRef"/>.</summary>
    internal static Dictionary<string, object?> Metadata(string targetRef) => new()
    {
        ["ApiType"] = "Azure",
        ["ResourceId"] = $"${{{targetRef}.id}}",
        ["location"] = $"${{{targetRef}.location}}",
    };

    /// <summary>Grants the project identity a built-in role on <paramref name="scope"/>; returns the assignment's Pulumi name.</summary>
    internal string Role(string name, string roleDefinitionId, string scope)
    {
        // Idempotent: the same role on the same resource (e.g. Storage from a connection and from the capability host) is assigned once.
        var id = $"{ProjectId}{name}";
        return Program.Resources.ContainsKey(id) ? id : Program.AddRoleAssignment(id, ProjectPrincipalId, roleDefinitionId, scope);
    }

    /// <summary>Grants the project identity the built-in Cosmos DB Data Contributor role on the account.</summary>
    internal string CosmosDataContributor(AzureCosmosDBResource cosmos)
    {
        var name = $"{ProjectId}{cosmos.Id}CosmosDataContributor";
        Program.Resources[name] = new Dictionary<string, object?>
        {
            ["type"] = "azure-native:cosmosdb:SqlResourceSqlRoleAssignment",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = PulumiExpr.Select(4, PulumiExpr.Split("/", $"${{{cosmos.Id}.id}}")),
                ["accountName"] = $"${{{cosmos.Id}.name}}",
                ["roleAssignmentId"] = new Guid(MD5.HashData(Encoding.UTF8.GetBytes(name))).ToString(),
                ["principalId"] = ProjectPrincipalId,
                ["roleDefinitionId"] = $"${{{cosmos.Id}.id}}/sqlRoleDefinitions/{AzureCosmosDBResource.DataContributorRoleId}",
                ["scope"] = $"${{{cosmos.Id}.id}}",
            },
        };
        return name;
    }
}

/// <summary>Role definition ids used by the project connections that are not in the shared list.</summary>
internal static class FoundryRoleIds
{
    public const string SearchIndexDataContributor = "8ebe5a00-799e-43f5-93ac-243d3dce84a7";
    public const string SearchServiceContributor = "7ca78c08-252a-4471-8644-bb5ff32d4ba0";
}

/// <summary>Target/category of the AAD connections a project (capability host) makes to the supported resources.</summary>
internal static class FoundryResourceTargets
{
    internal sealed record Target(string Ref, string Category, string TargetValue);

    public static Target Storage(AzureStorageResource storage) => new(storage.Id, "AzureStorageAccount", $"${{{storage.Id}.primaryEndpoints.blob}}");
    public static Target Cosmos(AzureCosmosDBResource cosmos) => new(cosmos.Id, "CosmosDb", $"${{{cosmos.Id}.documentEndpoint}}");
    public static Target Search(AzureSearchResource search) => new(search.Id, "CognitiveSearch", $"https://${{{search.Id}.name}}.search.windows.net");
    public static Target AzureOpenAI(FoundryResource account) => new(account.AccountRef, "AzureOpenAI", $"${{{account.AccountRef}.properties.endpoint}}");
}

/// <summary>Resources a project capability host (<see cref="FoundryProjectExtensions.AddCapabilityHost"/>) is wired to.</summary>
public sealed class FoundryCapabilityHostConfiguration(string name)
{
    public string Name { get; } = name;

    /// <summary>Cosmos DB account used for thread storage (required).</summary>
    public AzureCosmosDBResource? CosmosDB { get; internal set; }

    /// <summary>Storage account used for file storage (required).</summary>
    public AzureStorageResource? Storage { get; internal set; }

    /// <summary>Search service used for the vector store (required).</summary>
    public AzureSearchResource? Search { get; internal set; }

    /// <summary>Account whose Azure OpenAI models agents may use (optional).</summary>
    public FoundryResource? AzureOpenAI { get; internal set; }

    internal void Validate(string projectName)
    {
        if (CosmosDB is null) throw new InvalidOperationException($"Capability host '{Name}' on project '{projectName}' requires a CosmosDB resource. Call WithCosmosDB() on the capability host builder.");
        if (Storage is null) throw new InvalidOperationException($"Capability host '{Name}' on project '{projectName}' requires a Storage resource. Call WithStorage() on the capability host builder.");
        if (Search is null) throw new InvalidOperationException($"Capability host '{Name}' on project '{projectName}' requires a Search resource. Call WithSearch() on the capability host builder.");
    }
}

/// <summary>Builder returned by <c>AddCapabilityHost</c>.</summary>
public sealed class FoundryCapabilityHostBuilder(IResourceBuilder<FoundryProjectResource> projectBuilder, FoundryCapabilityHostConfiguration configuration)
{
    public IResourceBuilder<FoundryProjectResource> ProjectBuilder { get; } = projectBuilder;

    public FoundryCapabilityHostBuilder WithCosmosDB(IResourceBuilder<AzureCosmosDBResource> cosmosDb)
    {
        ArgumentNullException.ThrowIfNull(cosmosDb);
        configuration.CosmosDB = cosmosDb.Resource;
        return this;
    }

    public FoundryCapabilityHostBuilder WithStorage(IResourceBuilder<AzureStorageResource> storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        configuration.Storage = storage.Resource;
        return this;
    }

    public FoundryCapabilityHostBuilder WithSearch(IResourceBuilder<AzureSearchResource> search)
    {
        ArgumentNullException.ThrowIfNull(search);
        configuration.Search = search.Resource;
        return this;
    }

    public FoundryCapabilityHostBuilder WithAzureOpenAI(IResourceBuilder<FoundryResource> openAi)
    {
        ArgumentNullException.ThrowIfNull(openAi);
        configuration.AzureOpenAI = openAi.Resource;
        return this;
    }
}
