using Aspire.Hosting.ApplicationModel;

namespace AspirePulumiPackages.Pulumi.Azure.Network;

public enum SecurityRuleDirection { Inbound, Outbound }

public enum SecurityRuleAccess { Allow, Deny }

/// <summary>Network protocol of a security rule; <see cref="Any"/> renders as '*'.</summary>
public enum SecurityRuleProtocol { Any, Tcp, Udp, Icmp, Esp, Ah }

public enum NetworkSecurityPerimeterAccessRuleDirection { Inbound, Outbound }

public enum NetworkSecurityPerimeterAssociationAccessMode { Enforced, Learning, Audit }

/// <summary>Well-known Azure service tags usable as security rule source/destination prefixes.</summary>
public static class AzureServiceTags
{
    public const string Internet = "Internet";
    public const string VirtualNetwork = "VirtualNetwork";
    public const string AzureLoadBalancer = "AzureLoadBalancer";
    public const string AzureTrafficManager = "AzureTrafficManager";
    public const string Storage = "Storage";
    public const string Sql = "Sql";
    public const string AzureCosmosDB = "AzureCosmosDB";
    public const string AzureKeyVault = "AzureKeyVault";
    public const string EventHub = "EventHub";
    public const string ServiceBus = "ServiceBus";
    public const string AzureContainerRegistry = "AzureContainerRegistry";
    public const string AppService = "AppService";
    public const string AzureActiveDirectory = "AzureActiveDirectory";
    public const string AzureMonitor = "AzureMonitor";
    public const string GatewayManager = "GatewayManager";
}

/// <summary>A network security group rule (mirrors Aspire's AzureSecurityRule).</summary>
public sealed class AzureSecurityRule
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int Priority { get; set; }
    public SecurityRuleDirection Direction { get; set; }
    public SecurityRuleAccess Access { get; set; }
    public SecurityRuleProtocol Protocol { get; set; } = SecurityRuleProtocol.Any;
    public string SourceAddressPrefix { get; set; } = "*";
    public string SourcePortRange { get; set; } = "*";
    public string DestinationAddressPrefix { get; set; } = "*";
    public string DestinationPortRange { get; set; } = "*";
}

/// <summary>A network security perimeter access rule (mirrors Aspire's AzureNspAccessRule).</summary>
public sealed class AzureNspAccessRule
{
    public required string Name { get; set; }
    public NetworkSecurityPerimeterAccessRuleDirection Direction { get; set; }
    public List<string> AddressPrefixes { get; } = [];
    public List<string> Subscriptions { get; } = [];
    public List<string> FullyQualifiedDomainNames { get; } = [];
}

/// <summary>A Pulumi-provisioned Azure resource with an ARM resource id (as a Pulumi expression, e.g. <c>${vnet.id}</c>).</summary>
public interface IPulumiAzureResourceWithId : IResource
{
    /// <summary>Pulumi interpolation expression yielding the Azure resource id.</summary>
    string IdExpression { get; }
}

/// <summary>
/// A resource a private endpoint can be created for (the equivalent of Aspire's IAzurePrivateEndpointTarget).
/// Implement on a Pulumi Azure resource to make it usable with <c>AddPrivateEndpoint</c>.
/// </summary>
public interface IPulumiPrivateEndpointTarget : IPulumiAzureResourceWithId
{
    /// <summary>The private link sub-resource group id (e.g. 'blob', 'sqlServer', 'vault').</summary>
    string PrivateLinkGroupId { get; }

    /// <summary>The private DNS zone for the group (e.g. 'privatelink.blob.core.windows.net').</summary>
    string PrivateDnsZoneName { get; }
}

/// <summary>Records that a compute resource uses a subnet (WithDelegatedSubnet) so it can reference the subnet id.</summary>
public sealed class PulumiDelegatedSubnetAnnotation(PulumiSubnetResource subnet) : IResourceAnnotation
{
    public PulumiSubnetResource Subnet { get; } = subnet;
}

internal static class Yaml
{
    public static Dictionary<string, object?> Map(params (string Key, object? Value)[] entries)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in entries) if (v is not null) d[k] = v;
        return d;
    }

    public static Dictionary<string, object?> Resource(string type, Dictionary<string, object?> properties, params string[] dependsOn)
    {
        var d = new Dictionary<string, object?> { ["type"] = type, ["properties"] = properties };
        if (dependsOn.Length > 0) d["options"] = new Dictionary<string, object?> { ["dependsOn"] = dependsOn.Select(x => (object?)x).ToList() };
        return d;
    }

    public static Dictionary<string, object?> Tags(string aspireName) => new() { ["aspire-resource-name"] = aspireName };
}
