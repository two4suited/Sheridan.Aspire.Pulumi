using System.Text.RegularExpressions;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure.ContainerRegistry;
using Sheridan.Aspire.Pulumi.Azure.Kubernetes;
using Sheridan.Aspire.Pulumi.Azure.Network;
using Sheridan.Aspire.Pulumi.Azure.OperationalInsights;

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

    /// <summary>
    /// Uses an existing AKS cluster instead of provisioning one. Its node pools, networking and add-ons are never changed, and
    /// cluster-creation settings (<c>WithSystemNodePool</c>, <c>WithSubnet</c>, <c>WithNetworkProfile</c>, ...) are ignored. The registry,
    /// AcrPull assignment for the cluster's kubelet identity and workload identity credentials are still provisioned. The cluster must have a
    /// kubelet identity, and OIDC plus workload identity when workloads use them (otherwise call <c>WithWorkloadIdentity(false)</c>).
    /// Mirrors <c>AsExisting</c>. <paramref name="resourceGroupName"/> selects the cluster's group independently of the group supporting
    /// resources go to; null means the environment's own group.
    /// </summary>
    public static IResourceBuilder<AzureKubernetesEnvironmentResource> AsExisting(
        this IResourceBuilder<AzureKubernetesEnvironmentResource> builder, string existingResourceName, string? resourceGroupName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(existingResourceName);
        builder.Resource.ExistingClusterName = existingResourceName;
        builder.Resource.ExistingClusterResourceGroup = resourceGroupName;
        return builder;
    }

    /// <summary>Alias of <see cref="AsExisting"/> (the Pulumi resource has no separate local mode). Mirrors <c>PublishAsExisting</c>.</summary>
    public static IResourceBuilder<AzureKubernetesEnvironmentResource> PublishAsExisting(
        this IResourceBuilder<AzureKubernetesEnvironmentResource> builder, string existingResourceName, string? resourceGroupName) =>
        builder.AsExisting(existingResourceName, resourceGroupName);

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
    /// Enables (default) or disables the OIDC issuer and workload identity on the cluster. Mirrors <c>WithWorkloadIdentity</c>:
    /// each compute resource deployed to the cluster gets a service account (<c>{name}-sa</c>) federated to the environment's
    /// shared user-assigned identity, so role assignments granted to the environment apply to its workloads.
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

    /// <summary>
    /// Sets the cluster's network profile (plugin, policy, service CIDR, DNS service IP). Upstream only models this internally
    /// (defaults: Azure CNI, Calico, 10.0.4.0/22, 10.0.4.10); here it is a typed option. The plugin and policy cannot change after creation.
    /// </summary>
    public static IResourceBuilder<AzureKubernetesEnvironmentResource> WithNetworkProfile(
        this IResourceBuilder<AzureKubernetesEnvironmentResource> builder, string networkPlugin = "azure", string? networkPolicy = "calico",
        string serviceCidr = "10.0.4.0/22", string dnsServiceIP = "10.0.4.10")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(networkPlugin);
        ArgumentException.ThrowIfNullOrEmpty(serviceCidr);
        ArgumentException.ThrowIfNullOrEmpty(dnsServiceIP);
        builder.Resource.NetworkProfile = new AksNetworkProfile(networkPlugin, networkPolicy, serviceCidr, dnsServiceIP);
        return builder;
    }

    /// <summary>
    /// Makes the API server private (<c>apiServerAccessProfile.enablePrivateCluster</c>), using the AKS-managed private DNS zone. Upstream models this
    /// internally without a public setter; here it is a typed option. Note that the deploying machine then needs network access to the cluster's VNet.
    /// </summary>
    public static IResourceBuilder<AzureKubernetesEnvironmentResource> AsPrivateCluster(this IResourceBuilder<AzureKubernetesEnvironmentResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Resource.IsPrivateCluster = true;
        return builder;
    }

    /// <summary>
    /// Makes the API server private and registers it in a custom private DNS zone (named like <c>privatelink.{region}.azmk8s.io</c>). The control plane
    /// then runs as a user-assigned identity with Private DNS Zone Contributor on the zone and Network Contributor on its virtual network.
    /// </summary>
    public static IResourceBuilder<AzureKubernetesEnvironmentResource> AsPrivateCluster(
        this IResourceBuilder<AzureKubernetesEnvironmentResource> builder, IResourceBuilder<PulumiPrivateDnsZoneResource> privateDnsZone)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(privateDnsZone);
        builder.Resource.IsPrivateCluster = true;
        builder.Resource.PrivateDnsZone = privateDnsZone.Resource;
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
