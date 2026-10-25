using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using AspirePulumiPackages.Pulumi.Azure.Base;

namespace AspirePulumiPackages.Pulumi.Azure.Network;

/// <summary>
/// An Azure virtual network provisioned by Pulumi (azure-native), with its subnets as child <c>network:Subnet</c> resources
/// (chained with dependsOn so they are created one at a time, as the Bicep does).
/// </summary>
public sealed class PulumiVirtualNetworkResource(string name, string addressPrefix) : PulumiAzureResource(name), IPulumiAzureResourceWithId
{
    public string AddressPrefix { get; } = addressPrefix;
    public List<PulumiSubnetResource> Subnets { get; } = [];

    public string IdExpression => $"${{{Id}.id}}";

    public PulumiOutputReference IdOutput => Output(Stack ?? throw Unbound(), "Id");
    public PulumiOutputReference NameOutput => Output(Stack ?? throw Unbound(), "Name");

    private InvalidOperationException Unbound() => new($"'{Name}' has not been bound to a Pulumi stack yet.");

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string resourceGroup, string location)
    {
        program.AddInfraResource(Id, "azure-native:network:VirtualNetwork", Yaml.Resource("azure-native:network:VirtualNetwork", Yaml.Map(
            ("resourceGroupName", resourceGroup),
            ("location", location),
            ("addressSpace", Yaml.Map(("addressPrefixes", new List<object?> { AddressPrefix }))),
            ("tags", Yaml.Tags(Name)))));

        string? previous = null;
        foreach (var subnet in Subnets)
        {
            var props = Yaml.Map(
                ("resourceGroupName", resourceGroup),
                ("virtualNetworkName", $"${{{Id}.name}}"),
                ("subnetName", subnet.SubnetName),
                ("addressPrefix", subnet.AddressPrefix));

            if (subnet.Delegations.Count > 0)
                props["delegations"] = subnet.Delegations
                    .Select(d => (object?)Yaml.Map(("name", d.Name), ("serviceName", d.ServiceName))).ToList();
            if (subnet.NatGateway is { } nat)
                props["natGateway"] = Yaml.Map(("id", nat.IdExpression));
            if (subnet.NetworkSecurityGroup is { } nsg)
                props["networkSecurityGroup"] = Yaml.Map(("id", nsg.IdExpression));

            program.AddInfraResource(subnet.PulumiId, "azure-native:network:Subnet",
                previous is null
                    ? Yaml.Resource("azure-native:network:Subnet", props)
                    : Yaml.Resource("azure-native:network:Subnet", props, $"${{{previous}}}"));
            previous = subnet.PulumiId;
            program.Outputs[$"{subnet.PulumiId}Id"] = $"${{{subnet.PulumiId}.id}}";
        }

        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        return Task.CompletedTask;
    }
}

/// <summary>A subnet of a <see cref="PulumiVirtualNetworkResource"/>; provisioned by its parent network.</summary>
public sealed class PulumiSubnetResource(string name, string subnetName, string addressPrefix, PulumiVirtualNetworkResource parent)
    : Resource(name), IPulumiAzureResourceWithId
{
    public PulumiVirtualNetworkResource Parent { get; } = parent;
    public string SubnetName { get; } = subnetName;
    public string AddressPrefix { get; } = addressPrefix;
    public List<(string ServiceName, string Name)> Delegations { get; } = [];
    public PulumiNatGatewayResource? NatGateway { get; set; }
    public PulumiNetworkSecurityGroupResource? NetworkSecurityGroup { get; set; }

    /// <summary>Pulumi identifier of the subnet resource (vnet id + subnet id).</summary>
    public string PulumiId => Parent.Id + PulumiAzureProgramExtensions.ToPulumiId(Name);

    public string IdExpression => $"${{{PulumiId}.id}}";

    /// <summary>The subnet's resource id as a stack output reference (output <c>{vnet}{subnet}Id</c>).</summary>
    public PulumiOutputReference IdOutput => new(
        Parent.Stack ?? throw new InvalidOperationException($"'{Parent.Name}' has not been bound to a Pulumi stack yet."), $"{PulumiId}Id");
}

/// <summary>A network security group with its security rules.</summary>
public sealed class PulumiNetworkSecurityGroupResource(string name) : PulumiAzureResource(name), IPulumiAzureResourceWithId
{
    public List<AzureSecurityRule> SecurityRules { get; } = [];

    public string IdExpression => $"${{{Id}.id}}";

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string resourceGroup, string location)
    {
        program.AddInfraResource(Id, "azure-native:network:NetworkSecurityGroup", Yaml.Resource("azure-native:network:NetworkSecurityGroup", Yaml.Map(
            ("resourceGroupName", resourceGroup),
            ("location", location),
            ("tags", Yaml.Tags(Name)))));

        foreach (var rule in SecurityRules)
        {
            var ruleId = Id + PulumiAzureProgramExtensions.ToPulumiId(rule.Name.Replace('/', '-'));
            program.AddInfraResource(ruleId, "azure-native:network:SecurityRule", Yaml.Resource("azure-native:network:SecurityRule", Yaml.Map(
                ("resourceGroupName", resourceGroup),
                ("networkSecurityGroupName", $"${{{Id}.name}}"),
                ("securityRuleName", rule.Name),
                ("description", rule.Description),
                ("priority", rule.Priority),
                ("direction", rule.Direction.ToString()),
                ("access", rule.Access.ToString()),
                ("protocol", rule.Protocol == SecurityRuleProtocol.Any ? "*" : rule.Protocol.ToString()),
                ("sourceAddressPrefix", rule.SourceAddressPrefix),
                ("sourcePortRange", rule.SourcePortRange),
                ("destinationAddressPrefix", rule.DestinationAddressPrefix),
                ("destinationPortRange", rule.DestinationPortRange))));
        }

        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        return Task.CompletedTask;
    }
}

/// <summary>A Standard-SKU, statically allocated public IP address.</summary>
public sealed class PulumiPublicIPAddressResource(string name) : PulumiAzureResource(name), IPulumiAzureResourceWithId
{
    public string IdExpression => $"${{{Id}.id}}";

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string resourceGroup, string location)
    {
        program.AddInfraResource(Id, "azure-native:network:PublicIPAddress", Yaml.Resource("azure-native:network:PublicIPAddress", Yaml.Map(
            ("resourceGroupName", resourceGroup),
            ("location", location),
            ("publicIPAllocationMethod", "Static"),
            ("sku", Yaml.Map(("name", "Standard"))),
            ("tags", Yaml.Tags(Name)))));

        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        program.Outputs[$"{Id}IpAddress"] = $"${{{Id}.ipAddress}}";
        return Task.CompletedTask;
    }
}

/// <summary>A Standard-SKU NAT gateway attached to one or more public IP addresses.</summary>
public sealed class PulumiNatGatewayResource(string name) : PulumiAzureResource(name), IPulumiAzureResourceWithId
{
    public List<PulumiPublicIPAddressResource> PublicIPAddresses { get; } = [];

    public string IdExpression => $"${{{Id}.id}}";

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string resourceGroup, string location)
    {
        var props = Yaml.Map(
            ("resourceGroupName", resourceGroup),
            ("location", location),
            ("sku", Yaml.Map(("name", "Standard"))),
            ("tags", Yaml.Tags(Name)));
        if (PublicIPAddresses.Count > 0)
            props["publicIpAddresses"] = PublicIPAddresses.Select(p => (object?)Yaml.Map(("id", p.IdExpression))).ToList();

        program.AddInfraResource(Id, "azure-native:network:NatGateway", Yaml.Resource("azure-native:network:NatGateway", props));
        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        return Task.CompletedTask;
    }
}

/// <summary>A private DNS zone linked to a virtual network (no auto-registration).</summary>
public sealed class PulumiPrivateDnsZoneResource(string name, string zoneName, PulumiVirtualNetworkResource virtualNetwork)
    : PulumiAzureResource(name), IPulumiAzureResourceWithId
{
    public string ZoneName { get; } = zoneName;
    public PulumiVirtualNetworkResource VirtualNetwork { get; } = virtualNetwork;

    public string IdExpression => $"${{{Id}.id}}";

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string resourceGroup, string location)
    {
        program.AddInfraResource(Id, "azure-native:privatedns:PrivateZone", Yaml.Resource("azure-native:privatedns:PrivateZone", Yaml.Map(
            ("resourceGroupName", resourceGroup),
            ("privateZoneName", ZoneName),
            ("location", "global"),
            ("tags", Yaml.Tags(Name)))));

        var linkId = $"{Id}VnetLink";
        program.AddInfraResource(linkId, "azure-native:privatedns:VirtualNetworkLink", Yaml.Resource("azure-native:privatedns:VirtualNetworkLink", Yaml.Map(
            ("resourceGroupName", resourceGroup),
            ("privateZoneName", $"${{{Id}.name}}"),
            ("virtualNetworkLinkName", "vnet-link"),
            ("location", "global"),
            ("registrationEnabled", false),
            ("virtualNetwork", Yaml.Map(("id", VirtualNetwork.IdExpression))),
            ("tags", Yaml.Tags($"{Name}-vnet-link")))));

        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        return Task.CompletedTask;
    }
}

/// <summary>A private endpoint for a <see cref="IPulumiPrivateEndpointTarget"/> in a subnet, registered in a private DNS zone.</summary>
public sealed class PulumiPrivateEndpointResource(string name, PulumiSubnetResource subnet, IPulumiPrivateEndpointTarget target, PulumiPrivateDnsZoneResource dnsZone)
    : PulumiAzureResource(name), IPulumiAzureResourceWithId
{
    public PulumiSubnetResource Subnet { get; } = subnet;
    public IPulumiPrivateEndpointTarget Target { get; } = target;
    public PulumiPrivateDnsZoneResource DnsZone { get; } = dnsZone;

    public string IdExpression => $"${{{Id}.id}}";

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string resourceGroup, string location)
    {
        program.AddInfraResource(Id, "azure-native:network:PrivateEndpoint", Yaml.Resource("azure-native:network:PrivateEndpoint", Yaml.Map(
            ("resourceGroupName", resourceGroup),
            ("location", location),
            ("subnet", Yaml.Map(("id", Subnet.IdExpression))),
            ("privateLinkServiceConnections", new List<object?>
            {
                Yaml.Map(
                    ("name", $"{Name}-connection"),
                    ("privateLinkServiceId", Target.IdExpression),
                    ("groupIds", new List<object?> { Target.PrivateLinkGroupId })),
            }),
            ("tags", Yaml.Tags(Name)))));

        program.AddInfraResource($"{Id}DnsGroup", "azure-native:network:PrivateDnsZoneGroup", Yaml.Resource("azure-native:network:PrivateDnsZoneGroup", Yaml.Map(
            ("resourceGroupName", resourceGroup),
            ("privateEndpointName", $"${{{Id}.name}}"),
            ("privateDnsZoneGroupName", "default"),
            ("privateDnsZoneConfigs", new List<object?>
            {
                Yaml.Map(("name", DnsZone.ZoneName.Replace('.', '_')), ("privateDnsZoneId", DnsZone.IdExpression)),
            }))));

        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        return Task.CompletedTask;
    }
}

/// <summary>An existing/other Azure resource id exposed as a private endpoint target (for resources without their own integration).</summary>
public sealed class PulumiPrivateLinkTargetResource(string name, string resourceIdExpression, string privateLinkGroupId, string privateDnsZoneName)
    : Resource(name), IPulumiPrivateEndpointTarget
{
    public string IdExpression { get; } = resourceIdExpression;
    public string PrivateLinkGroupId { get; } = privateLinkGroupId;
    public string PrivateDnsZoneName { get; } = privateDnsZoneName;
}

/// <summary>A network security perimeter with a default profile, access rules and resource associations.</summary>
public sealed class PulumiNetworkSecurityPerimeterResource(string name) : PulumiAzureResource(name), IPulumiAzureResourceWithId
{
    public List<AzureNspAccessRule> AccessRules { get; } = [];
    public List<(IPulumiAzureResourceWithId Target, NetworkSecurityPerimeterAssociationAccessMode AccessMode, string AssociationName)> Associations { get; } = [];

    public string IdExpression => $"${{{Id}.id}}";

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string resourceGroup, string location)
    {
        program.AddInfraResource(Id, "azure-native:network:NetworkSecurityPerimeter", Yaml.Resource("azure-native:network:NetworkSecurityPerimeter", Yaml.Map(
            ("resourceGroupName", resourceGroup),
            ("location", location),
            ("tags", Yaml.Tags(Name)))));

        var profileId = $"{Id}Profile";
        program.AddInfraResource(profileId, "azure-native:network:NspProfile", Yaml.Resource("azure-native:network:NspProfile", Yaml.Map(
            ("resourceGroupName", resourceGroup),
            ("networkSecurityPerimeterName", $"${{{Id}.name}}"),
            ("profileName", "defaultProfile"))));

        foreach (var rule in AccessRules)
        {
            var props = Yaml.Map(
                ("resourceGroupName", resourceGroup),
                ("networkSecurityPerimeterName", $"${{{Id}.name}}"),
                ("profileName", $"${{{profileId}.name}}"),
                ("accessRuleName", rule.Name),
                ("direction", rule.Direction.ToString()));
            if (rule.AddressPrefixes.Count > 0) props["addressPrefixes"] = rule.AddressPrefixes.Cast<object?>().ToList();
            if (rule.FullyQualifiedDomainNames.Count > 0) props["fullyQualifiedDomainNames"] = rule.FullyQualifiedDomainNames.Cast<object?>().ToList();
            if (rule.Subscriptions.Count > 0)
                props["subscriptions"] = rule.Subscriptions
                    .Select(s => (object?)Yaml.Map(("id", s.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase) ? s : $"/subscriptions/{s}"))).ToList();

            program.AddInfraResource(profileId + PulumiAzureProgramExtensions.ToPulumiId(rule.Name), "azure-native:network:NspAccessRule",
                Yaml.Resource("azure-native:network:NspAccessRule", props));
        }

        foreach (var (target, mode, associationName) in Associations)
        {
            var associationId = $"{Id}{PulumiAzureProgramExtensions.ToPulumiId(associationName)}";
            program.AddInfraResource(associationId, "azure-native:network:NspAssociation", Yaml.Resource("azure-native:network:NspAssociation", Yaml.Map(
                ("resourceGroupName", resourceGroup),
                ("networkSecurityPerimeterName", $"${{{Id}.name}}"),
                ("associationName", associationName),
                ("accessMode", mode.ToString()),
                ("privateLinkResource", Yaml.Map(("id", target.IdExpression))),
                ("profile", Yaml.Map(("id", $"${{{profileId}.id}}"))))));
        }

        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        return Task.CompletedTask;
    }
}
