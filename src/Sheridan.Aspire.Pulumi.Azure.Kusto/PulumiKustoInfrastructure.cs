using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Provisioning;

namespace Sheridan.Aspire.Pulumi.Azure.Kusto;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi Kusto cluster. Port of the Azure.Provisioning
/// Kusto overlay (root: <c>KustoCluster</c>). Properties use azure-native names, e.g.
/// <c>KustoCluster.Set("enableStreamingIngest", true)</c>.
/// </summary>
public sealed class PulumiKustoInfrastructure(PulumiInfrastructure inner, AzureKustoClusterResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:kusto:Cluster</c>.</summary>
    public PulumiResourceNode KustoCluster => GetResource(owner.Id);

    /// <summary>The <c>azure-native:kusto:ReadWriteDatabase</c> resources of this cluster.</summary>
    public IEnumerable<PulumiResourceNode> Databases =>
        GetResources("azure-native:kusto:ReadWriteDatabase").Where(r => owner.Databases.Any(d => r.Id == $"{owner.Id}{d.Id}"));
}

public static class PulumiKustoInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the Kusto resources before the program is emitted.</summary>
    public static IResourceBuilder<AzureKustoClusterResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureKustoClusterResource> builder, Action<PulumiKustoInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiKustoInfrastructure(infra, owner)));
    }
}
