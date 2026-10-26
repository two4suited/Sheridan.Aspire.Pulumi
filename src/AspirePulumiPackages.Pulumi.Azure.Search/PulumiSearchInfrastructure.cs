using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.Search;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi Search service. Port of the Azure.Provisioning
/// Search overlay (root: <c>SearchService</c>). Properties use azure-native names, e.g.
/// <c>SearchService.Set("replicaCount", 3)</c>.
/// </summary>
public sealed class PulumiSearchInfrastructure(PulumiInfrastructure inner, AzureSearchResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:search:Service</c>.</summary>
    public PulumiResourceNode SearchService => GetResource(owner.Id);
}

public static class PulumiSearchInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the Search resources before the program is emitted.</summary>
    public static IResourceBuilder<AzureSearchResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureSearchResource> builder, Action<PulumiSearchInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiSearchInfrastructure(infra, owner)));
    }
}
