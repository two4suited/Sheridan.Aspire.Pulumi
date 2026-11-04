using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Provisioning;

namespace Sheridan.Aspire.Pulumi.Azure.Network;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi private DNS zone. Port of the Azure.Provisioning
/// PrivateDns overlay (root: <c>PrivateDnsZone</c>). Properties use azure-native names, e.g.
/// <c>PrivateDnsZone.Set("tags.team", "platform")</c>.
/// </summary>
public sealed class PulumiPrivateDnsInfrastructure(PulumiInfrastructure inner, PulumiPrivateDnsZoneResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:privatedns:PrivateZone</c>.</summary>
    public PulumiResourceNode PrivateDnsZone => GetResource(owner.Id);

    /// <summary>The <c>azure-native:privatedns:VirtualNetworkLink</c> linking the zone to its virtual network.</summary>
    public PulumiResourceNode VirtualNetworkLink => GetResource($"{owner.Id}VnetLink");
}

public static class PulumiPrivateDnsInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the private DNS zone resources before the program is emitted.</summary>
    public static IResourceBuilder<PulumiPrivateDnsZoneResource> ConfigureInfrastructure(
        this IResourceBuilder<PulumiPrivateDnsZoneResource> builder, Action<PulumiPrivateDnsInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiPrivateDnsInfrastructure(infra, owner)));
    }
}
