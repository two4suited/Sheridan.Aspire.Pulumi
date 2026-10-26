using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Azure.Kubernetes;
using AspirePulumiPackages.Pulumi.Provisioning;

namespace Aspire.Hosting;

/// <summary>
/// Editable view of the AKS environment's Pulumi resources (overlay of <c>ConfigureInfrastructure</c>). Property paths follow the
/// azure-native schema, e.g. <c>Set("apiServerAccessProfile.enablePrivateCluster", true)</c> on the managed cluster.
/// </summary>
public sealed class PulumiKubernetesInfrastructure(PulumiInfrastructure inner, AzureKubernetesEnvironmentResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:containerservice:ManagedCluster</c> (upstream <c>ContainerServiceManagedCluster</c>).</summary>
    public PulumiResourceNode ContainerServiceManagedCluster => GetResource(owner.Id);

    /// <summary>The environment's own container registry (not available when <c>WithContainerRegistry</c> supplied one).</summary>
    public PulumiResourceNode ContainerRegistryService => owner.Registry is null
        ? GetResource($"{owner.Id}Registry")
        : throw new InvalidOperationException($"'{owner.Name}' uses an external registry; configure it on its own resource.");
}

public static class PulumiKubernetesInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of <c>ConfigureInfrastructure</c>: customize the AKS resources before the program is emitted.</summary>
    public static IResourceBuilder<AzureKubernetesEnvironmentResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureKubernetesEnvironmentResource> builder, Action<PulumiKubernetesInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiKubernetesInfrastructure(infra, owner)));
    }
}
