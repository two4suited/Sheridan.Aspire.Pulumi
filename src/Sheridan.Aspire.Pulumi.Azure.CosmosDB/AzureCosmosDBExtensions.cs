using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Utils;
using Sheridan.Aspire.Pulumi.Azure.Base;
using Sheridan.Aspire.Pulumi.Azure.CosmosDB;

namespace Aspire.Hosting;

/// <summary>Pulumi counterparts of the extension methods in the official <c>Aspire.Hosting.Azure.CosmosDB</c> package.</summary>
public static class AzureCosmosDBExtensions
{
    /// <summary>Adds an Azure Cosmos DB (NoSQL API) account that the Pulumi stack provisions. Mirrors <c>AddAzureCosmosDB</c>.</summary>
    public static IResourceBuilder<AzureCosmosDBResource> AddPulumiCosmosDB(this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new AzureCosmosDBResource(name)).ExcludeFromManifest();
    }

    /// <summary>Alias of <see cref="AddPulumiCosmosDB"/> following the <c>AddPulumiAzureX</c> naming used by the other Azure integrations.</summary>
    public static IResourceBuilder<AzureCosmosDBResource> AddPulumiAzureCosmosDB(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiCosmosDB(name);

    /// <summary>Adds a SQL (NoSQL API) database to the account. <paramref name="databaseName"/> defaults to <paramref name="name"/>.</summary>
    public static IResourceBuilder<AzureCosmosDBDatabaseResource> AddCosmosDatabase(
        this IResourceBuilder<AzureCosmosDBResource> builder, string name, string? databaseName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var database = new AzureCosmosDBDatabaseResource(name, databaseName ?? name, builder.Resource);
        builder.Resource.Databases.Add(database);
        return builder.ApplicationBuilder.AddResource(database).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>Alias of <see cref="AddCosmosDatabase"/> (the official integration's <c>AddDatabase</c>).</summary>
    public static IResourceBuilder<AzureCosmosDBDatabaseResource> AddDatabase(
        this IResourceBuilder<AzureCosmosDBResource> builder, string name) =>
        builder.AddCosmosDatabase(name);

    /// <summary>Adds a container with a single partition key path (e.g. "/id") to the database. <paramref name="containerName"/> defaults to <paramref name="name"/>.</summary>
    public static IResourceBuilder<AzureCosmosDBContainerResource> AddContainer(
        this IResourceBuilder<AzureCosmosDBDatabaseResource> builder, string name, string partitionKeyPath, string? containerName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(partitionKeyPath);
        return builder.AddContainer(name, [partitionKeyPath], containerName);
    }

    /// <summary>Adds a container with a hierarchical (composite) partition key to the database.</summary>
    public static IResourceBuilder<AzureCosmosDBContainerResource> AddContainer(
        this IResourceBuilder<AzureCosmosDBDatabaseResource> builder, string name, IEnumerable<string> partitionKeyPaths, string? containerName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(partitionKeyPaths);
        var paths = partitionKeyPaths.ToList();
        if (paths.Count == 0) throw new ArgumentException("At least one partition key path should be provided.", nameof(partitionKeyPaths));
        if (paths.Any(string.IsNullOrEmpty)) throw new ArgumentException("Partition key paths cannot contain null or empty strings.", nameof(partitionKeyPaths));

        var container = new AzureCosmosDBContainerResource(name, containerName ?? name, paths, builder.Resource);
        builder.Resource.Containers.Add(container);
        return builder.ApplicationBuilder.AddResource(container).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>
    /// Authenticates with access keys instead of Entra identities: local auth stays enabled and the connection strings and
    /// primary key are written as secrets to a Key Vault the stack creates for the account. Mirrors <c>WithAccessKeyAuthentication</c>
    /// (the overload taking an existing vault is not supported).
    /// </summary>
    public static IResourceBuilder<AzureCosmosDBResource> WithAccessKeyAuthentication(this IResourceBuilder<AzureCosmosDBResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Resource.UseAccessKeyAuthentication = true;
        return builder;
    }

    /// <summary>Deploys the account with the Azure default (provisioned throughput) SKU instead of serverless. Mirrors <c>WithDefaultAzureSku</c>.</summary>
    public static IResourceBuilder<AzureCosmosDBResource> WithDefaultAzureSku(this IResourceBuilder<AzureCosmosDBResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Resource.Serverless = false;
        return builder;
    }

    /// <summary>Runs the Linux-based (vNext) Cosmos DB emulator locally instead of the Pulumi-provisioned account. No effect in publish mode.</summary>
    public static IResourceBuilder<AzureCosmosDBResource> RunAsEmulator(
        this IResourceBuilder<AzureCosmosDBResource> builder, Action<IResourceBuilder<AzureCosmosDBEmulatorResource>>? configureContainer = null) =>
        RunAs(builder, classic: false, tag: "vnext-latest", configureContainer);

    /// <summary>Runs the preview Linux-based (vNext) emulator locally. Currently the same image as <see cref="RunAsEmulator"/>.</summary>
    public static IResourceBuilder<AzureCosmosDBResource> RunAsPreviewEmulator(
        this IResourceBuilder<AzureCosmosDBResource> builder, Action<IResourceBuilder<AzureCosmosDBEmulatorResource>>? configureContainer = null) =>
        RunAs(builder, classic: false, tag: "vnext-latest", configureContainer);

    /// <summary>Runs the classic Cosmos DB emulator locally instead of the Pulumi-provisioned account. No effect in publish mode.</summary>
    public static IResourceBuilder<AzureCosmosDBResource> RunAsClassicEmulator(
        this IResourceBuilder<AzureCosmosDBResource> builder, Action<IResourceBuilder<AzureCosmosDBEmulatorResource>>? configureContainer = null) =>
        RunAs(builder, classic: true, tag: "latest", configureContainer);

    private static IResourceBuilder<AzureCosmosDBResource> RunAs(
        IResourceBuilder<AzureCosmosDBResource> builder, bool classic, string tag, Action<IResourceBuilder<AzureCosmosDBEmulatorResource>>? configureContainer)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.ApplicationBuilder.ExecutionContext.IsPublishMode) return builder;

        var emulator = new AzureCosmosDBEmulatorResource(builder.Resource) { IsClassic = classic };
        var emulatorBuilder = builder.ApplicationBuilder.AddResource(emulator)
            .WithImage("cosmosdb/linux/azure-cosmos-emulator", tag)
            .WithImageRegistry("mcr.microsoft.com")
            .WithHttpsEndpoint(targetPort: 8081, name: "emulator")
            .WithParentRelationship(builder)
            .ExcludeFromManifest();

        if (classic)
        {
            emulatorBuilder.WithEnvironment("AZURE_COSMOS_EMULATOR_ENABLE_DATA_PERSISTENCE", "false");
        }
        else
        {
            emulatorBuilder
                .WithHttpEndpoint(targetPort: 8080, name: "emulatorhealth")
                .WithEnvironment("PROTOCOL", "https")
                .WithEnvironment("ENABLE_EXPLORER", "false");
        }

        builder.Resource.Emulator = emulator;
        if (!classic)
        {
            // The vNext emulator exposes a readiness probe; the databases and containers are created once it passes.
            emulatorBuilder.WithHttpHealthCheck("/ready", endpointName: "emulatorhealth");
        }
        CosmosDBEmulatorInitializer.Register(builder.ApplicationBuilder, builder.Resource, emulator);
        configureContainer?.Invoke(emulatorBuilder);
        return builder;
    }

    /// <summary>Adds a named volume for the emulator's data folder (and turns on data persistence).</summary>
    public static IResourceBuilder<AzureCosmosDBEmulatorResource> WithDataVolume(
        this IResourceBuilder<AzureCosmosDBEmulatorResource> builder, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var volume = name ?? VolumeNameGenerator.Generate(builder, "data");
        if (builder.Resource.IsClassic)
            return builder.WithVolume(volume, "/tmp/cosmos/appdata").WithEnvironment("AZURE_COSMOS_EMULATOR_ENABLE_DATA_PERSISTENCE", "true");
        return builder.WithVolume(volume, "/data");
    }

    /// <summary>Binds the emulator gateway to a fixed host port.</summary>
    public static IResourceBuilder<AzureCosmosDBEmulatorResource> WithGatewayPort(
        this IResourceBuilder<AzureCosmosDBEmulatorResource> builder, int? port)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithEndpoint("emulator", e => e.Port = port);
    }

    /// <summary>Sets the partition count of the classic emulator (1-250). Not supported by the vNext emulator.</summary>
    public static IResourceBuilder<AzureCosmosDBEmulatorResource> WithPartitionCount(
        this IResourceBuilder<AzureCosmosDBEmulatorResource> builder, int count)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!builder.Resource.IsClassic)
            throw new InvalidOperationException("'WithPartitionCount' does not work when using the Linux-based (vNext) Azure Cosmos DB emulator.");
        if (count is < 1 or > 250) throw new ArgumentOutOfRangeException(nameof(count), "Count must be between 1 and 250.");
        return builder.WithEnvironment("AZURE_COSMOS_EMULATOR_PARTITION_COUNT", count.ToString());
    }

    /// <summary>Exposes the vNext emulator's Data Explorer endpoint (optionally on a fixed host port).</summary>
    public static IResourceBuilder<AzureCosmosDBEmulatorResource> WithDataExplorer(
        this IResourceBuilder<AzureCosmosDBEmulatorResource> builder, int? port = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Resource.IsClassic)
            throw new InvalidOperationException("The Data Explorer endpoint is only available when using the Linux-based (vNext) Azure Cosmos DB emulator. Call 'RunAsEmulator' instead.");
        return builder
            .WithEnvironment("ENABLE_EXPLORER", "true")
            .WithEnvironment("EXPLORER_PROTOCOL", "http")
            .WithHttpEndpoint(port: port, targetPort: 1234, name: "data-explorer");
    }
}
