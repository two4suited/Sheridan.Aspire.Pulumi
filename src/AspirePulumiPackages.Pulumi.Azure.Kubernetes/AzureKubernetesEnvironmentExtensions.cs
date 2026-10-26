using System.Text.RegularExpressions;
using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Azure.ContainerRegistry;
using AspirePulumiPackages.Pulumi.Azure.Kubernetes;
using AspirePulumiPackages.Pulumi.Azure.Network;
using AspirePulumiPackages.Pulumi.Azure.OperationalInsights;

namespace Aspire.Hosting;

/// <summary>Pulumi counterparts of the extension methods in the official <c>Aspire.Hosting.Azure.Kubernetes</c> package.</summary>
public static partial class AzureKubernetesEnvironmentExtensions
{
    /// <summary>Adds an AKS cluster that the Pulumi stack provisions. Mirrors <c>AddAzureKubernetesEnvironment</c>.</summary>
    public static IResourceBuilder<AzureKubernetesEnvironmentResource> AddPulumiAzureKubernetesEnvironment(
        this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new AzureKubernetesEnvironmentResource(name)).ExcludeFromManifest();
    }

    /// <summary>Alias for <see cref="AddPulumiAzureKubernetesEnvironment"/>.</summary>
    public static IResourceBuilder<AzureKubernetesEnvironmentResource> AddPulumiKubernetes(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiAzureKubernetesEnvironment(name);

    /// <summary>Replaces the default system node pool. Mirrors <c>WithSystemNodePool</c> (defaults: Standard_D2s_v5, 1-3 nodes).</summary>
    public static IResourceBuilder<AzureKubernetesEnvironmentResource> WithSystemNodePool(
        this IResourceBuilder<AzureKubernetesEnvironmentResource> builder, string vmSize = "Standard_D2s_v5", int minCount = 1, int maxCount = 3)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidatePool(vmSize, minCount, maxCount);
        builder.Resource.SystemNodePool = new AksNodePoolConfig(AzureKubernetesEnvironmentResource.SystemPoolName, vmSize, minCount, maxCount, AksNodePoolMode.System);
        return builder;
    }

    /// <summary>Adds a user node pool. Mirrors <c>AddNodePool</c> (defaults: Standard_D2s_v5, 1-3 nodes).</summary>
    public static IResourceBuilder<AksNodePoolResource> AddNodePool(
        this IResourceBuilder<AzureKubernetesEnvironmentResource> builder, string name, string vmSize = "Standard_D2s_v5", int minCount = 1, int maxCount = 3)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!PoolName().IsMatch(name))
            throw new ArgumentException("A node pool name must start with a lowercase letter and contain only lowercase letters and digits (max 12).", nameof(name));
        if (name == "system" || builder.Resource.NodePools.Any(p => p.Config.Name == name))
            throw new ArgumentException($"A node pool named '{name}' already exists on '{builder.Resource.Name}'.", nameof(name));
        ValidatePool(vmSize, minCount, maxCount);
        var pool = new AksNodePoolResource(name, new AksNodePoolConfig(name, vmSize, minCount, maxCount, AksNodePoolMode.User), builder.Resource);
        builder.Resource.NodePools.Add(pool);
        return builder.ApplicationBuilder.AddResource(pool).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>
    /// Places the cluster's node pools in a VNet subnet (a plain, non-delegated subnet from the Network package). Mirrors <c>WithSubnet</c>.
    /// </summary>
    public static IResourceBuilder<AzureKubernetesEnvironmentResource> WithSubnet(
        this IResourceBuilder<AzureKubernetesEnvironmentResource> builder, IResourceBuilder<PulumiSubnetResource> subnet)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(subnet);
        builder.Resource.Subnet = subnet.Resource;
        return builder;
    }

    /// <summary>Places a single node pool in its own subnet, overriding the environment-level one. Mirrors <c>WithSubnet</c>.</summary>
    public static IResourceBuilder<AksNodePoolResource> WithSubnet(
        this IResourceBuilder<AksNodePoolResource> builder, IResourceBuilder<PulumiSubnetResource> subnet)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(subnet);
        builder.Resource.Subnet = subnet.Resource;
        return builder;
    }

    /// <summary>Uses a Pulumi container registry instead of the environment's own. Mirrors <c>WithContainerRegistry</c>.</summary>
    public static IResourceBuilder<AzureKubernetesEnvironmentResource> WithContainerRegistry(
        this IResourceBuilder<AzureKubernetesEnvironmentResource> builder, IResourceBuilder<AzureContainerRegistryResource> registry)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(registry);
        builder.Resource.Registry = registry.Resource;
        return builder;
    }

    /// <summary>
    /// Enables (default) or disables the OIDC issuer and workload identity on the cluster. Mirrors <c>WithWorkloadIdentity</c>
    /// (federated credentials for compute workloads are not generated, since this port deploys no workloads).
    /// </summary>
    public static IResourceBuilder<AzureKubernetesEnvironmentResource> WithWorkloadIdentity(
        this IResourceBuilder<AzureKubernetesEnvironmentResource> builder, bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Resource.WorkloadIdentityEnabled = enabled;
        builder.Resource.OidcIssuerEnabled = enabled;
        return builder;
    }

    /// <summary>
    /// Enables Container Insights, sending cluster logs and metrics to a Pulumi Log Analytics workspace. Not part of the
    /// official public surface (upstream exposes it only as a resource property); provided for the Log Analytics wiring.
    /// </summary>
    public static IResourceBuilder<AzureKubernetesEnvironmentResource> WithContainerInsights(
        this IResourceBuilder<AzureKubernetesEnvironmentResource> builder, IResourceBuilder<LogAnalyticsWorkspaceResource> workspace)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(workspace);
        builder.Resource.LogAnalyticsWorkspace = workspace.Resource;
        return builder;
    }

    private static void ValidatePool(string vmSize, int minCount, int maxCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vmSize);
        if (minCount < 0) throw new ArgumentOutOfRangeException(nameof(minCount));
        if (maxCount < minCount || maxCount < 1) throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be at least 1 and not less than minCount.");
    }

    [GeneratedRegex("^[a-z][a-z0-9]{0,11}$")]
    private static partial Regex PoolName();
}
