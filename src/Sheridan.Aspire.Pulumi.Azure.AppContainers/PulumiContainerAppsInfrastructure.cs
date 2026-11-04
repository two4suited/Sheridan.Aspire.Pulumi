using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Provisioning;

namespace Sheridan.Aspire.Pulumi.Azure.AppContainers;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi Container Apps environment. Port of the Azure.Provisioning
/// AppContainers overlay (root: <c>ContainerAppManagedEnvironment</c>; apps and jobs via identifier lookup). Properties use
/// azure-native names, e.g. <c>ContainerAppManagedEnvironment.Set("zoneRedundant", true)</c>.
/// </summary>
public sealed class PulumiContainerAppsInfrastructure(PulumiInfrastructure inner, AzureContainerAppEnvironmentResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:app:ManagedEnvironment</c>.</summary>
    public PulumiResourceNode ContainerAppManagedEnvironment => GetResource(owner.Id);

    /// <summary>The <c>azure-native:app:ContainerApp</c> (or <c>azure-native:app:Job</c>) emitted for the compute resource with the given Aspire name.</summary>
    public PulumiResourceNode GetContainerApp(string computeResourceName) => GetResource(AzureContainerAppEnvironmentResource.AppId(computeResourceName));
}

public static class PulumiContainerAppsInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the Container Apps resources before the program is emitted.</summary>
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureContainerAppEnvironmentResource> builder, Action<PulumiContainerAppsInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiContainerAppsInfrastructure(infra, owner)));
    }
}
