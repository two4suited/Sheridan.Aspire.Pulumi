using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace AspirePulumiPackages.Pulumi.Azure.Kusto;

/// <summary>Pulumi equivalents of the Aspire.Hosting.Azure.Kusto extension methods.</summary>
public static class AzureKustoExtensions
{
    /// <summary>Adds an Azure Data Explorer (Kusto) cluster provisioned by the Pulumi stack (the equivalent of AddAzureKustoCluster).</summary>
    public static IResourceBuilder<AzureKustoClusterResource> AddPulumiKustoCluster(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddResource(new AzureKustoClusterResource(name)).ExcludeFromManifest();

    /// <summary>Alias of <see cref="AddPulumiKustoCluster"/> following the <c>AddPulumiAzureX</c> naming used by the other Azure integrations.</summary>
    public static IResourceBuilder<AzureKustoClusterResource> AddPulumiAzureKustoCluster(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiKustoCluster(name);

    /// <summary>Alias of <see cref="AddPulumiKustoCluster"/> (the name used in the issue tracker).</summary>
    public static IResourceBuilder<AzureKustoClusterResource> AddPulumiKusto(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiKustoCluster(name);

    /// <summary>Overrides the cluster SKU (default Standard_E2a_v4 / Standard / 2 instances).</summary>
    public static IResourceBuilder<AzureKustoClusterResource> WithSku(
        this IResourceBuilder<AzureKustoClusterResource> builder, string name, string tier = "Standard", int capacity = 2)
    {
        builder.Resource.SkuName = name;
        builder.Resource.SkuTier = tier;
        builder.Resource.SkuCapacity = capacity;
        return builder;
    }

    /// <summary>Adds a read-write database to the cluster (<paramref name="databaseName"/> defaults to <paramref name="name"/>).</summary>
    public static IResourceBuilder<AzureKustoReadWriteDatabaseResource> AddReadWriteDatabase(
        this IResourceBuilder<AzureKustoClusterResource> builder, string name, string? databaseName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var resource = new AzureKustoReadWriteDatabaseResource(name, databaseName ?? name, builder.Resource);
        builder.Resource.Databases.Add(resource);
        return builder.ApplicationBuilder.AddResource(resource).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>Sets KQL control commands to run against the database once it exists (the official <c>WithCreationScript</c>).</summary>
    public static IResourceBuilder<AzureKustoReadWriteDatabaseResource> WithCreationScript(
        this IResourceBuilder<AzureKustoReadWriteDatabaseResource> builder, string script)
    {
        ArgumentException.ThrowIfNullOrEmpty(script);
        builder.Resource.CreationScript = script;
        return builder;
    }
}
