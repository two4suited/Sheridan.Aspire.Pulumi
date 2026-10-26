using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.Network;

// Port of the Azure.Provisioning Network overlay. Upstream lookup roots: VirtualNetwork, NetworkSecurityGroup, NatGateway,
// PublicIPAddress, PrivateEndpoint, NetworkSecurityPerimeter. Each Pulumi resource exposes its own root; property paths are
// azure-native input names, e.g. VirtualNetwork.Set("enableDdosProtection", true).

/// <summary>Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi virtual network (root: <c>VirtualNetwork</c>).</summary>
public sealed class PulumiVirtualNetworkInfrastructure(PulumiInfrastructure inner, PulumiVirtualNetworkResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:network:VirtualNetwork</c>.</summary>
    public PulumiResourceNode VirtualNetwork => GetResource(owner.Id);

    /// <summary>The <c>azure-native:network:Subnet</c> resources of this network.</summary>
    public IEnumerable<PulumiResourceNode> Subnets => owner.Subnets.Select(s => GetResource(s.PulumiId));
}

/// <summary>Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi network security group (root: <c>NetworkSecurityGroup</c>).</summary>
public sealed class PulumiNetworkSecurityGroupInfrastructure(PulumiInfrastructure inner, PulumiNetworkSecurityGroupResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:network:NetworkSecurityGroup</c>.</summary>
    public PulumiResourceNode NetworkSecurityGroup => GetResource(owner.Id);

    /// <summary>The <c>azure-native:network:SecurityRule</c> resources of this group.</summary>
    public IEnumerable<PulumiResourceNode> SecurityRules =>
        GetResources("azure-native:network:SecurityRule").Where(r => r.Id.StartsWith(owner.Id, StringComparison.Ordinal));
}

/// <summary>Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi NAT gateway (root: <c>NatGateway</c>).</summary>
public sealed class PulumiNatGatewayInfrastructure(PulumiInfrastructure inner, PulumiNatGatewayResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:network:NatGateway</c>.</summary>
    public PulumiResourceNode NatGateway => GetResource(owner.Id);
}

/// <summary>Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi public IP address (root: <c>PublicIPAddress</c>).</summary>
public sealed class PulumiPublicIPAddressInfrastructure(PulumiInfrastructure inner, PulumiPublicIPAddressResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:network:PublicIPAddress</c>.</summary>
    public PulumiResourceNode PublicIPAddress => GetResource(owner.Id);
}

/// <summary>Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi private endpoint (root: <c>PrivateEndpoint</c>).</summary>
public sealed class PulumiPrivateEndpointInfrastructure(PulumiInfrastructure inner, PulumiPrivateEndpointResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:network:PrivateEndpoint</c>.</summary>
    public PulumiResourceNode PrivateEndpoint => GetResource(owner.Id);

    /// <summary>The <c>azure-native:network:PrivateDnsZoneGroup</c> registering the endpoint in its DNS zone.</summary>
    public PulumiResourceNode PrivateDnsZoneGroup => GetResource($"{owner.Id}DnsGroup");
}

/// <summary>Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi network security perimeter (root: <c>NetworkSecurityPerimeter</c>).</summary>
public sealed class PulumiNetworkSecurityPerimeterInfrastructure(PulumiInfrastructure inner, PulumiNetworkSecurityPerimeterResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:network:NetworkSecurityPerimeter</c>.</summary>
    public PulumiResourceNode NetworkSecurityPerimeter => GetResource(owner.Id);

    /// <summary>The perimeter's default <c>azure-native:network:NspProfile</c>.</summary>
    public PulumiResourceNode Profile => GetResource($"{owner.Id}Profile");

    /// <summary>The <c>azure-native:network:NspAccessRule</c> resources of the perimeter.</summary>
    public IEnumerable<PulumiResourceNode> AccessRules => GetResources("azure-native:network:NspAccessRule");

    /// <summary>The <c>azure-native:network:NspAssociation</c> resources of the perimeter.</summary>
    public IEnumerable<PulumiResourceNode> Associations => GetResources("azure-native:network:NspAssociation");
}

public static class PulumiNetworkInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c> for a virtual network.</summary>
    public static IResourceBuilder<PulumiVirtualNetworkResource> ConfigureInfrastructure(
        this IResourceBuilder<PulumiVirtualNetworkResource> builder, Action<PulumiVirtualNetworkInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiVirtualNetworkInfrastructure(infra, owner)));
    }

    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c> for a network security group.</summary>
    public static IResourceBuilder<PulumiNetworkSecurityGroupResource> ConfigureInfrastructure(
        this IResourceBuilder<PulumiNetworkSecurityGroupResource> builder, Action<PulumiNetworkSecurityGroupInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiNetworkSecurityGroupInfrastructure(infra, owner)));
    }

    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c> for a NAT gateway.</summary>
    public static IResourceBuilder<PulumiNatGatewayResource> ConfigureInfrastructure(
        this IResourceBuilder<PulumiNatGatewayResource> builder, Action<PulumiNatGatewayInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiNatGatewayInfrastructure(infra, owner)));
    }

    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c> for a public IP address.</summary>
    public static IResourceBuilder<PulumiPublicIPAddressResource> ConfigureInfrastructure(
        this IResourceBuilder<PulumiPublicIPAddressResource> builder, Action<PulumiPublicIPAddressInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiPublicIPAddressInfrastructure(infra, owner)));
    }

    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c> for a private endpoint.</summary>
    public static IResourceBuilder<PulumiPrivateEndpointResource> ConfigureInfrastructure(
        this IResourceBuilder<PulumiPrivateEndpointResource> builder, Action<PulumiPrivateEndpointInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiPrivateEndpointInfrastructure(infra, owner)));
    }

    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c> for a network security perimeter.</summary>
    public static IResourceBuilder<PulumiNetworkSecurityPerimeterResource> ConfigureInfrastructure(
        this IResourceBuilder<PulumiNetworkSecurityPerimeterResource> builder, Action<PulumiNetworkSecurityPerimeterInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiNetworkSecurityPerimeterInfrastructure(infra, owner)));
    }
}
