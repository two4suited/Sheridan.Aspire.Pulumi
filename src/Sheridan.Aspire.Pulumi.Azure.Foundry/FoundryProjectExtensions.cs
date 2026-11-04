using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure.ApplicationInsights;
using Sheridan.Aspire.Pulumi.Azure.Base;
using Sheridan.Aspire.Pulumi.Azure.ContainerRegistry;
using Sheridan.Aspire.Pulumi.Azure.CosmosDB;
using Sheridan.Aspire.Pulumi.Azure.KeyVault;
using Sheridan.Aspire.Pulumi.Azure.Search;
using Sheridan.Aspire.Pulumi.Azure.Storage;

namespace Sheridan.Aspire.Pulumi.Azure.Foundry;

/// <summary>Pulumi equivalents of the Foundry project extension methods (connections, capability host, model deployments).</summary>
public static class FoundryProjectExtensions
{
    /// <summary>Adds a model deployment to the project's account (mirrors <c>AddModelDeployment</c>).</summary>
    public static IResourceBuilder<FoundryDeploymentResource> AddModelDeployment(
        this IResourceBuilder<FoundryProjectResource> builder, string name, FoundryModel model)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.ApplicationBuilder.CreateResourceBuilder(builder.Resource.Parent).AddDeployment(name, model);
    }

    /// <inheritdoc cref="AddModelDeployment(IResourceBuilder{FoundryProjectResource}, string, FoundryModel)"/>
    public static IResourceBuilder<FoundryDeploymentResource> AddModelDeployment(
        this IResourceBuilder<FoundryProjectResource> builder, string name, string modelName, string modelVersion, string format)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.ApplicationBuilder.CreateResourceBuilder(builder.Resource.Parent).AddDeployment(name, modelName, modelVersion, format);
    }

    /// <summary>
    /// Sends the project's telemetry to this Application Insights resource (which must be provisioned in the same Pulumi stack)
    /// instead of a component created for the project. Mirrors <c>WithAppInsights</c>.
    /// </summary>
    public static IResourceBuilder<FoundryProjectResource> WithAppInsights(
        this IResourceBuilder<FoundryProjectResource> builder, IResourceBuilder<PulumiApplicationInsightsResource> appInsights)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(appInsights);
        builder.Resource.AppInsights = appInsights.Resource;
        return builder;
    }

    /// <summary>
    /// Connects the project to a Key Vault (same stack) and grants the project identity Key Vault Secrets Officer on it; every other
    /// connection of the project depends on this one. Mirrors <c>WithKeyVault</c>.
    /// </summary>
    public static IResourceBuilder<FoundryProjectResource> WithKeyVault(
        this IResourceBuilder<FoundryProjectResource> builder, IResourceBuilder<PulumiAzureKeyVaultResource> keyVault)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(keyVault);
        if (builder.Resource.KeyVaultConnection is not null)
            throw new InvalidOperationException($"Foundry project resource '{builder.Resource.Name}' already has a Key Vault connection configured.");
        var vault = keyVault.Resource;
        var connection = builder.AddConnectionCore($"{vault.Name}-{Hash(builder.Resource, vault)}", ConnectionName(vault.Name, builder.Resource, vault),
            _ => new Dictionary<string, object?>
            {
                ["authType"] = "ManagedIdentity",
                ["category"] = "AzureKeyVault",
                ["isSharedToAll"] = true,
                ["target"] = $"${{{VaultRef(vault)}.id}}",
                ["metadata"] = FoundryConnectionContext.Metadata(VaultRef(vault)),
            },
            ctx => ctx.Role($"{vault.Id}KeyVaultSecretsOfficer", AzureRoleDefinitions.KeyVaultSecretsOfficer, $"${{{VaultRef(vault)}.id}}"));
        builder.Resource.KeyVaultConnection = connection.Resource;
        return builder;
    }

    /// <summary>Connects the project to a Storage account (blob endpoint, AAD) and grants the project identity Storage Blob Data Contributor.</summary>
    public static IResourceBuilder<FoundryProjectConnectionResource> AddConnection(
        this IResourceBuilder<FoundryProjectResource> builder, IResourceBuilder<AzureStorageResource> storage)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(storage);
        var st = storage.Resource;
        return builder.AddConnectionCore($"connection-{Hash(builder.Resource, st)}", ConnectionName("connection", builder.Resource, st),
            _ => new Dictionary<string, object?>
            {
                ["authType"] = "AAD",
                ["category"] = "AzureBlob",
                ["isSharedToAll"] = true,
                ["target"] = $"${{{st.Id}.primaryEndpoints.blob}}",
                ["metadata"] = new Dictionary<string, object?> { ["ApiType"] = "Azure", ["ResourceId"] = $"${{{st.Id}.id}}" },
            },
            ctx => ctx.Role($"{st.Id}BlobDataContributor", AzureRoleDefinitions.StorageBlobDataContributor, $"${{{st.Id}.id}}"));
    }

    /// <summary>Connects the project to a Cosmos DB account (document endpoint, AAD).</summary>
    public static IResourceBuilder<FoundryProjectConnectionResource> AddConnection(
        this IResourceBuilder<FoundryProjectResource> builder, IResourceBuilder<AzureCosmosDBResource> cosmos)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(cosmos);
        var db = cosmos.Resource;
        return builder.AddConnectionCore($"connection-{Hash(builder.Resource, db)}", ConnectionName("connection", builder.Resource, db),
            _ => new Dictionary<string, object?>
            {
                ["authType"] = "AAD",
                ["category"] = "CosmosDb",
                ["isSharedToAll"] = true,
                ["target"] = $"${{{db.Id}.documentEndpoint}}",
                ["metadata"] = new Dictionary<string, object?> { ["ApiType"] = "Azure", ["ResourceId"] = $"${{{db.Id}.id}}" },
            });
    }

    /// <summary>Connects the project to a Search service (AAD) and grants the project identity Search Index Data Contributor and Search Service Contributor.</summary>
    public static IResourceBuilder<FoundryProjectConnectionResource> AddConnection(
        this IResourceBuilder<FoundryProjectResource> builder, IResourceBuilder<AzureSearchResource> search)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(search);
        var srch = search.Resource;
        return builder.AddConnectionCore($"connection-{Hash(builder.Resource, srch)}", ConnectionName("connection", builder.Resource, srch),
            _ => new Dictionary<string, object?>
            {
                ["authType"] = "AAD",
                ["category"] = "CognitiveSearch",
                ["target"] = $"https://${{{srch.Id}.name}}.search.windows.net",
                ["metadata"] = FoundryConnectionContext.Metadata(srch.Id),
            },
            ctx =>
            {
                ctx.Role($"{srch.Id}SearchIndexDataContributor", FoundryRoleIds.SearchIndexDataContributor, $"${{{srch.Id}.id}}");
                ctx.Role($"{srch.Id}SearchServiceContributor", FoundryRoleIds.SearchServiceContributor, $"${{{srch.Id}.id}}");
            });
    }

    /// <summary>
    /// Connects the project to a Container Registry (managed identity). Like the official integration this uses the
    /// <c>aiprojectidentityprincipleaid</c> client id placeholder and the registry name as resource id.
    /// </summary>
    public static IResourceBuilder<FoundryProjectConnectionResource> AddConnection(
        this IResourceBuilder<FoundryProjectResource> builder, IResourceBuilder<AzureContainerRegistryResource> registry)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(registry);
        var acr = registry.Resource;
        return builder.AddConnectionCore($"connection-{Hash(builder.Resource, acr)}", ConnectionName("connection", builder.Resource, acr),
            _ => new Dictionary<string, object?>
            {
                ["authType"] = "ManagedIdentity",
                ["category"] = "ContainerRegistry",
                ["isSharedToAll"] = true,
                ["target"] = $"${{{acr.Id}.loginServer}}",
                ["credentials"] = new Dictionary<string, object?> { ["clientId"] = "aiprojectidentityprincipleaid", ["resourceId"] = $"${{{acr.Id}.name}}" },
                ["metadata"] = new Dictionary<string, object?> { ["ApiType"] = "Azure", ["ResourceId"] = $"${{{acr.Id}.name}}" },
            });
    }

    /// <summary>
    /// Adds a project connection with the given azure-native <c>ProjectConnection</c> <c>properties</c> (authType, category, target,
    /// credentials, metadata ...). The Pulumi counterpart of <c>AddConnection(name, configureProperties)</c>.
    /// </summary>
    public static IResourceBuilder<FoundryProjectConnectionResource> AddConnection(
        this IResourceBuilder<FoundryProjectResource> builder, string name, Func<FoundryConnectionContext, Dictionary<string, object?>> configureProperties)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(configureProperties);
        return builder.AddConnectionCore(name, name, configureProperties);
    }

    /// <summary>
    /// Adds a Bing grounding connection (an account connection, category <c>GroundingWithBingSearch</c>) to the Bing resource with the given ARM id.
    /// Unlike the official integration, which reads the key with <c>listKeys()</c> at deployment, azure-native cannot list Bing
    /// account keys, so the API key comes from a secret parameter (a secret Pulumi config entry).
    /// </summary>
    public static IResourceBuilder<FoundryProjectConnectionResource> AddBingGroundingConnection(
        this IResourceBuilder<FoundryProjectResource> builder, string name, string bingResourceId, IResourceBuilder<ParameterResource> apiKey)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(bingResourceId);
        ArgumentNullException.ThrowIfNull(apiKey);
        var key = PulumiAzureProgramExtensions.ToPulumiId(apiKey.Resource.Name);
        return builder.AddConnectionCore(name, name, ctx =>
        {
            Provisioning.PulumiProgramExtensions.AddParameter(ctx.Program, key, secret: true);
            return new Dictionary<string, object?>
            {
                ["authType"] = "ApiKey",
                ["category"] = "GroundingWithBingSearch",
                ["target"] = "https://api.bing.microsoft.com/",
                ["useWorkspaceManagedIdentity"] = false,
                ["isSharedToAll"] = false,
                ["peRequirement"] = "NotRequired",
                ["peStatus"] = "NotApplicable",
                ["credentials"] = new Dictionary<string, object?> { ["key"] = $"${{{key}}}" },
                ["metadata"] = new Dictionary<string, object?> { ["type"] = "bing_grounding", ["ApiType"] = "Azure", ["ResourceId"] = bingResourceId },
            };
        }, accountLevel: true);
    }

    /// <summary>
    /// Adds a capability host to the project; wire it to Cosmos DB (thread storage), Storage (files) and Search (vector store), and optionally an
    /// account for Azure OpenAI, with the returned builder. Mirrors <c>AddCapabilityHost</c> (the Pulumi program creates the
    /// <c>ProjectCapabilityHost</c>, the connections and their role assignments).
    /// </summary>
    public static FoundryCapabilityHostBuilder AddCapabilityHost(this IResourceBuilder<FoundryProjectResource> builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (builder.Resource.CapabilityHost is not null)
            throw new InvalidOperationException($"Foundry project resource '{builder.Resource.Name}' already has a capability host configured.");
        var configuration = new FoundryCapabilityHostConfiguration(name);
        builder.Resource.CapabilityHost = configuration;
        return new FoundryCapabilityHostBuilder(builder, configuration);
    }

    private static IResourceBuilder<FoundryProjectConnectionResource> AddConnectionCore(
        this IResourceBuilder<FoundryProjectResource> builder, string name, string connectionName,
        Func<FoundryConnectionContext, Dictionary<string, object?>> configure, Action<FoundryConnectionContext>? addRoleAssignments = null,
        bool accountLevel = false)
    {
        var connection = new FoundryProjectConnectionResource(name, connectionName, builder.Resource, configure, addRoleAssignments, accountLevel);
        builder.Resource.Connections.Add(connection);
        return builder.ApplicationBuilder.AddResource(connection).WithParentRelationship(builder).ExcludeFromManifest();
    }

    private static string VaultRef(PulumiAzureKeyVaultResource vault) => vault.ExistingVaultName is null ? vault.Id : $"{vault.Id}Existing";

    // Deterministic (the official integration uses a random guid, which would churn the Pulumi state on every run).
    private static string Hash(FoundryProjectResource project, IResource target) =>
        new Guid(MD5.HashData(Encoding.UTF8.GetBytes($"{project.Name}/{target.Name}"))).ToString("N");

    private static string ConnectionName(string prefix, FoundryProjectResource project, IResource target) => $"{prefix}-{Hash(project, target)}";
}
