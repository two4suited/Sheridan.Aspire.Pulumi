using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.CosmosDB;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi Cosmos DB account. Port of the Azure.Provisioning
/// CosmosDB overlay (root: <c>CosmosDBAccount</c>). Properties use azure-native names, e.g.
/// <c>CosmosDBAccount.Set("enableFreeTier", true)</c>.
/// </summary>
public sealed class PulumiCosmosDBInfrastructure(PulumiInfrastructure inner, AzureCosmosDBResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:cosmosdb:DatabaseAccount</c>.</summary>
    public PulumiResourceNode CosmosDBAccount => GetResource(owner.Id);

    /// <summary>The SQL databases (<c>azure-native:cosmosdb:SqlResourceSqlDatabase</c>) of the account.</summary>
    public IEnumerable<PulumiResourceNode> Databases => GetResources("azure-native:cosmosdb:SqlResourceSqlDatabase");

    /// <summary>The SQL containers (<c>azure-native:cosmosdb:SqlResourceSqlContainer</c>) of the account.</summary>
    public IEnumerable<PulumiResourceNode> Containers => GetResources("azure-native:cosmosdb:SqlResourceSqlContainer");
}

public static class PulumiCosmosDBInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the Cosmos DB resources before the program is emitted.</summary>
    public static IResourceBuilder<AzureCosmosDBResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureCosmosDBResource> builder, Action<PulumiCosmosDBInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiCosmosDBInfrastructure(infra, owner)));
    }
}
