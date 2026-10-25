using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi;

namespace AspirePulumiPackages.Pulumi.Azure;

public static class AzureCosmosDBExtensions
{
    /// <summary>Adds an Azure Cosmos DB (NoSQL API) account that the Pulumi stack provisions.</summary>
    public static IResourceBuilder<AzureCosmosDBAccountResource> AddPulumiAzureCosmosDB(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddResource(new AzureCosmosDBAccountResource(name)).ExcludeFromManifest();

    public static IResourceBuilder<AzureCosmosDBAccountResource> WithLocation(
        this IResourceBuilder<AzureCosmosDBAccountResource> builder, string location)
    {
        builder.Resource.Location = location;
        return builder;
    }

    public static IResourceBuilder<AzureCosmosDBAccountResource> WithPulumiStack(
        this IResourceBuilder<AzureCosmosDBAccountResource> builder, IResourceBuilder<PulumiStackResource> stack)
    {
        builder.Resource.Stack = stack.Resource;
        return builder;
    }

    /// <summary>Adds a SQL (NoSQL API) database to the account.</summary>
    public static IResourceBuilder<AzureCosmosDBDatabaseResource> AddCosmosDatabase(
        this IResourceBuilder<AzureCosmosDBAccountResource> builder, string name)
    {
        var database = new AzureCosmosDBDatabaseResource(name, builder.Resource);
        builder.Resource.Databases.Add(database);
        return builder.ApplicationBuilder.AddResource(database)
            .WithParentRelationship(builder)
            .ExcludeFromManifest();
    }

    /// <summary>Adds a container with a single partition key path (e.g. "/id") to the database.</summary>
    public static IResourceBuilder<AzureCosmosDBContainerResource> AddContainer(
        this IResourceBuilder<AzureCosmosDBDatabaseResource> builder, string name, string partitionKeyPath) =>
        builder.AddContainer(name, [partitionKeyPath]);

    /// <summary>Adds a container with a hierarchical (composite) partition key to the database.</summary>
    public static IResourceBuilder<AzureCosmosDBContainerResource> AddContainer(
        this IResourceBuilder<AzureCosmosDBDatabaseResource> builder, string name, IEnumerable<string> partitionKeyPaths)
    {
        var container = new AzureCosmosDBContainerResource(name, partitionKeyPaths.ToList(), builder.Resource);
        builder.Resource.Containers.Add(container);
        return builder.ApplicationBuilder.AddResource(container)
            .WithParentRelationship(builder)
            .ExcludeFromManifest();
    }

    /// <summary>
    /// Runs the Cosmos DB NoSQL API emulator locally instead of connecting to the Pulumi-provisioned account.
    /// Has no effect in publish mode. References to the account (<c>WithReference(cosmos)</c>) resolve to the
    /// emulator's endpoint and well-known key while running locally.
    /// </summary>
    public static IResourceBuilder<AzureCosmosDBAccountResource> RunAsEmulator(
        this IResourceBuilder<AzureCosmosDBAccountResource> builder)
    {
        if (builder.ApplicationBuilder.ExecutionContext.IsPublishMode) return builder;

        var emulator = new AzureCosmosDBEmulatorResource(builder.Resource);
        builder.ApplicationBuilder.AddResource(emulator)
            .WithImage("cosmosdb/linux/azure-cosmos-emulator", "vnext-latest")
            .WithImageRegistry("mcr.microsoft.com")
            .WithHttpsEndpoint(targetPort: 8081, name: "emulator")
            .WithHttpEndpoint(targetPort: 8080, name: "health")
            .WithEnvironment("PROTOCOL", "https")
            .WithEnvironment("ENABLE_EXPLORER", "false")
            .WithParentRelationship(builder)
            .ExcludeFromManifest();

        builder.Resource.Emulator = emulator;
        return builder;
    }
}
