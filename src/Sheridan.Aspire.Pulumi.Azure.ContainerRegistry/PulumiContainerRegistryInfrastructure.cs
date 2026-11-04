using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Provisioning;

namespace Sheridan.Aspire.Pulumi.Azure.ContainerRegistry;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi container registry. Port of the Azure.Provisioning
/// ContainerRegistry overlay (root: <c>ContainerRegistryService</c>). Properties use azure-native names, e.g.
/// <c>ContainerRegistryService.Set("adminUserEnabled", true)</c>.
/// </summary>
public sealed class PulumiContainerRegistryInfrastructure(PulumiInfrastructure inner, AzureContainerRegistryResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:containerregistry:Registry</c>.</summary>
    public PulumiResourceNode ContainerRegistryService => GetResource(owner.Id);
}

public static class PulumiContainerRegistryInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the registry resources before the program is emitted.</summary>
    public static IResourceBuilder<AzureContainerRegistryResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureContainerRegistryResource> builder, Action<PulumiContainerRegistryInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiContainerRegistryInfrastructure(infra, owner)));
    }
}
