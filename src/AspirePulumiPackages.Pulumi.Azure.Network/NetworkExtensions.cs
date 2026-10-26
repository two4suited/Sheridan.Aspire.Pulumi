using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Azure.Base;

namespace AspirePulumiPackages.Pulumi.Azure.Network;

/// <summary>
/// Pulumi equivalents of Aspire.Hosting.Azure.Network's extension methods (AddAzureVirtualNetwork becomes
/// AddPulumiVirtualNetwork, and so on; the <c>WithX</c> / subnet / rule methods keep their upstream names).
/// All network resources are expected to be provisioned by the same Pulumi stack because they reference each other by
/// Pulumi resource name. <c>WithLocation</c>, <c>WithPulumiStack</c> and <c>WithExistingResourceGroup</c> come from
/// <see cref="PulumiAzureExtensions"/>.
/// </summary>
public static class NetworkExtensions
{
    private const string DefaultAddressPrefix = "10.0.0.0/16";

    // ---- Virtual network & subnets ------------------------------------------------------------------------------------

    /// <summary>Adds a virtual network provisioned by Pulumi (AddAzureVirtualNetwork).</summary>
    public static IResourceBuilder<PulumiVirtualNetworkResource> AddPulumiVirtualNetwork(
        this IDistributedApplicationBuilder builder, string name, string? addressPrefix = null) =>
        builder.AddResource(new PulumiVirtualNetworkResource(name, addressPrefix ?? DefaultAddressPrefix)).ExcludeFromManifest();

    /// <summary>Adds a subnet to the virtual network. <paramref name="subnetName"/> defaults to <paramref name="name"/>.</summary>
    public static IResourceBuilder<PulumiSubnetResource> AddSubnet(
        this IResourceBuilder<PulumiVirtualNetworkResource> builder, string name, string addressPrefix, string? subnetName = null)
    {
        // An explicit subnet name is validated; otherwise it is the Aspire resource name, shortened with a hash only if it does not conform.
        var azureName = subnetName is null
            ? AzureNaming.ComposeScoped(AzureNameKind.Subnet, name)
            : AzureNaming.ValidateOverride(AzureNameKind.Subnet, subnetName, nameof(subnetName));
        var subnet = new PulumiSubnetResource(name, azureName, addressPrefix, builder.Resource);
        builder.Resource.Subnets.Add(subnet);
        return builder.ApplicationBuilder.AddResource(subnet)
            .WithParentRelationship(builder)
            .ExcludeFromManifest();
    }

    /// <summary>Delegates the subnet to an Azure service (e.g. 'Microsoft.App/environments'); <paramref name="name"/> defaults to the service name.</summary>
    public static IResourceBuilder<PulumiSubnetResource> WithServiceDelegation(
        this IResourceBuilder<PulumiSubnetResource> builder, string serviceName, string? name = null)
    {
        builder.Resource.Delegations.Add((serviceName, name ?? serviceName));
        return builder;
    }

    /// <summary>Attaches a NAT gateway to the subnet.</summary>
    public static IResourceBuilder<PulumiSubnetResource> WithNatGateway(
        this IResourceBuilder<PulumiSubnetResource> builder, IResourceBuilder<PulumiNatGatewayResource> natGateway)
    {
        builder.Resource.NatGateway = natGateway.Resource;
        return builder;
    }

    /// <summary>Attaches a network security group to the subnet (replacing any implicit one).</summary>
    public static IResourceBuilder<PulumiSubnetResource> WithNetworkSecurityGroup(
        this IResourceBuilder<PulumiSubnetResource> builder, IResourceBuilder<PulumiNetworkSecurityGroupResource> nsg)
    {
        builder.Resource.NetworkSecurityGroup = nsg.Resource;
        return builder;
    }

    /// <summary>
    /// Marks a compute resource (e.g. a container app environment) as using the subnet. Records a
    /// <see cref="PulumiDelegatedSubnetAnnotation"/> that compute integrations can read to wire up VNet integration.
    /// </summary>
    public static IResourceBuilder<T> WithDelegatedSubnet<T>(this IResourceBuilder<T> builder, IResourceBuilder<PulumiSubnetResource> subnet)
        where T : IResource
    {
        builder.WithAnnotation(new PulumiDelegatedSubnetAnnotation(subnet.Resource));
        return builder;
    }

    // ---- Subnet rule shortcuts (implicit '{subnet}-nsg') -----------------------------------------------------------------

    public static IResourceBuilder<PulumiSubnetResource> AllowInbound(this IResourceBuilder<PulumiSubnetResource> builder,
        string? port = null, string? from = null, string? to = null, SecurityRuleProtocol? protocol = null, int? priority = null, string? name = null) =>
        builder.AddRule(SecurityRuleAccess.Allow, SecurityRuleDirection.Inbound, port, from, to, protocol, priority, name);

    public static IResourceBuilder<PulumiSubnetResource> DenyInbound(this IResourceBuilder<PulumiSubnetResource> builder,
        string? port = null, string? from = null, string? to = null, SecurityRuleProtocol? protocol = null, int? priority = null, string? name = null) =>
        builder.AddRule(SecurityRuleAccess.Deny, SecurityRuleDirection.Inbound, port, from, to, protocol, priority, name);

    public static IResourceBuilder<PulumiSubnetResource> AllowOutbound(this IResourceBuilder<PulumiSubnetResource> builder,
        string? port = null, string? from = null, string? to = null, SecurityRuleProtocol? protocol = null, int? priority = null, string? name = null) =>
        builder.AddRule(SecurityRuleAccess.Allow, SecurityRuleDirection.Outbound, port, from, to, protocol, priority, name);

    public static IResourceBuilder<PulumiSubnetResource> DenyOutbound(this IResourceBuilder<PulumiSubnetResource> builder,
        string? port = null, string? from = null, string? to = null, SecurityRuleProtocol? protocol = null, int? priority = null, string? name = null) =>
        builder.AddRule(SecurityRuleAccess.Deny, SecurityRuleDirection.Outbound, port, from, to, protocol, priority, name);

    private static IResourceBuilder<PulumiSubnetResource> AddRule(this IResourceBuilder<PulumiSubnetResource> builder,
        SecurityRuleAccess access, SecurityRuleDirection direction, string? port, string? from, string? to,
        SecurityRuleProtocol? protocol, int? priority, string? name)
    {
        var subnet = builder.Resource;
        if (subnet.NetworkSecurityGroup is null)
        {
            var nsg = new PulumiNetworkSecurityGroupResource($"{subnet.Name}-nsg")
            {
                Stack = subnet.Parent.Stack,
                Location = subnet.Parent.Location,
            };
            builder.ApplicationBuilder.AddResource(nsg).WithParentRelationship(builder).ExcludeFromManifest();
            subnet.NetworkSecurityGroup = nsg;
        }

        var rules = subnet.NetworkSecurityGroup.SecurityRules;
        // Generated names embed ports and CIDRs ('*', '10.0.0.0/24'), which are not legal in rule names: sanitize (hashing when lossy).
        var ruleName = name ?? AzureNaming.ComposeScoped(AzureNameKind.NetworkSecurityRule, string.Join('-',
            new[] { access.ToString().ToLowerInvariant(), direction.ToString().ToLowerInvariant(), port, from, to }.Where(p => !string.IsNullOrEmpty(p))));

        rules.Add(new AzureSecurityRule
        {
            Name = ruleName,
            Access = access,
            Direction = direction,
            Priority = priority ?? NextPriority(rules),
            Protocol = protocol ?? SecurityRuleProtocol.Any,
            SourceAddressPrefix = from ?? "*",
            DestinationAddressPrefix = to ?? "*",
            DestinationPortRange = port ?? "*",
        });
        return builder;
    }

    private static int NextPriority(List<AzureSecurityRule> rules) => rules.Count == 0 ? 100 : rules.Max(r => r.Priority) + 10;

    // ---- NSG / public IP / NAT gateway ---------------------------------------------------------------------------------

    public static IResourceBuilder<PulumiNetworkSecurityGroupResource> AddPulumiNetworkSecurityGroup(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddResource(new PulumiNetworkSecurityGroupResource(name)).ExcludeFromManifest();

    public static IResourceBuilder<PulumiNetworkSecurityGroupResource> WithSecurityRule(
        this IResourceBuilder<PulumiNetworkSecurityGroupResource> builder, AzureSecurityRule rule)
    {
        builder.Resource.SecurityRules.Add(rule);
        return builder;
    }

    public static IResourceBuilder<PulumiPublicIPAddressResource> AddPulumiPublicIPAddress(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddResource(new PulumiPublicIPAddressResource(name)).ExcludeFromManifest();

    public static IResourceBuilder<PulumiNatGatewayResource> AddPulumiNatGateway(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddResource(new PulumiNatGatewayResource(name)).ExcludeFromManifest();

    public static IResourceBuilder<PulumiNatGatewayResource> WithPublicIPAddress(
        this IResourceBuilder<PulumiNatGatewayResource> builder, IResourceBuilder<PulumiPublicIPAddressResource> publicIPAddress)
    {
        builder.Resource.PublicIPAddresses.Add(publicIPAddress.Resource);
        return builder;
    }

    // ---- Private endpoints & DNS -----------------------------------------------------------------------------------------

    /// <summary>
    /// Gets or creates the private DNS zone (linked to <paramref name="virtualNetwork"/>) for a private link zone name such as
    /// 'privatelink.blob.core.windows.net'.
    /// </summary>
    public static IResourceBuilder<PulumiPrivateDnsZoneResource> GetOrCreatePrivateDnsZone(
        this IDistributedApplicationBuilder builder, string zoneName, PulumiVirtualNetworkResource virtualNetwork)
    {
        var existing = builder.Resources.OfType<PulumiPrivateDnsZoneResource>()
            .FirstOrDefault(z => z.ZoneName == zoneName && z.VirtualNetwork == virtualNetwork);
        if (existing is not null) return builder.CreateResourceBuilder(existing);

        var zone = new PulumiPrivateDnsZoneResource(zoneName.Replace('.', '-'), zoneName, virtualNetwork)
        {
            Stack = virtualNetwork.Stack,
            Location = virtualNetwork.Location,
        };
        return builder.AddResource(zone).ExcludeFromManifest();
    }

    /// <summary>Adds a private endpoint for the target in the subnet, plus the linked private DNS zone it registers in.</summary>
    public static IResourceBuilder<PulumiPrivateEndpointResource> AddPrivateEndpoint<TTarget>(
        this IResourceBuilder<PulumiSubnetResource> subnet, IResourceBuilder<TTarget> target)
        where TTarget : IResource, IPulumiPrivateEndpointTarget
    {
        var app = subnet.ApplicationBuilder;
        var zone = app.GetOrCreatePrivateDnsZone(target.Resource.PrivateDnsZoneName, subnet.Resource.Parent);
        var endpoint = new PulumiPrivateEndpointResource($"{subnet.Resource.Name}-{target.Resource.Name}-pe", subnet.Resource, target.Resource, zone.Resource)
        {
            Stack = subnet.Resource.Parent.Stack,
            Location = subnet.Resource.Parent.Location,
        };
        foreach (var extra in target.Resource.AdditionalPrivateDnsZoneNames)
            endpoint.AdditionalDnsZones.Add(app.GetOrCreatePrivateDnsZone(extra, subnet.Resource.Parent).Resource);
        return app.AddResource(endpoint)
            .WithParentRelationship(subnet)
            .ExcludeFromManifest();
    }

    /// <summary>
    /// Exposes an Azure resource id (a Pulumi expression such as <c>${other.id}</c> or a literal ARM id) as a private endpoint target,
    /// for resources whose own Pulumi integration does not implement <see cref="IPulumiPrivateEndpointTarget"/>.
    /// </summary>
    public static IResourceBuilder<PulumiPrivateLinkTargetResource> AddPulumiPrivateLinkTarget(
        this IDistributedApplicationBuilder builder, string name, string resourceIdExpression, string privateLinkGroupId, string privateDnsZoneName) =>
        builder.AddResource(new PulumiPrivateLinkTargetResource(name, resourceIdExpression, privateLinkGroupId, privateDnsZoneName)).ExcludeFromManifest();

    // ---- Network security perimeter --------------------------------------------------------------------------------------

    public static IResourceBuilder<PulumiNetworkSecurityPerimeterResource> AddPulumiNetworkSecurityPerimeter(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddResource(new PulumiNetworkSecurityPerimeterResource(name)).ExcludeFromManifest();

    public static IResourceBuilder<PulumiNetworkSecurityPerimeterResource> WithAccessRule(
        this IResourceBuilder<PulumiNetworkSecurityPerimeterResource> builder, AzureNspAccessRule rule)
    {
        builder.Resource.AccessRules.Add(rule);
        return builder;
    }

    /// <summary>Associates a Pulumi-provisioned Azure resource with the perimeter's default profile.</summary>
    public static IResourceBuilder<T> WithNetworkSecurityPerimeter<T>(this IResourceBuilder<T> target,
        IResourceBuilder<PulumiNetworkSecurityPerimeterResource> nsp,
        NetworkSecurityPerimeterAssociationAccessMode accessMode = NetworkSecurityPerimeterAssociationAccessMode.Enforced,
        string? associationName = null)
        where T : IResource, IPulumiAzureResourceWithId
    {
        nsp.Resource.Associations.Add((target.Resource, accessMode, associationName ?? $"{target.Resource.Name}-association"));
        return target;
    }
}
