using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.EventHubs;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi Event Hubs namespace. Port of the Azure.Provisioning
/// EventHubs overlay (root: <c>EventHubsNamespace</c>). Properties use azure-native names, e.g.
/// <c>EventHubsNamespace.Set("zoneRedundant", true)</c>.
/// </summary>
public sealed class PulumiEventHubsInfrastructure(PulumiInfrastructure inner, AzureEventHubsResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:eventhub:Namespace</c>.</summary>
    public PulumiResourceNode EventHubsNamespace => GetResource(owner.Id);

    /// <summary>The event hubs (<c>azure-native:eventhub:EventHub</c>) of the namespace.</summary>
    public IEnumerable<PulumiResourceNode> EventHubs => GetResources("azure-native:eventhub:EventHub");

    /// <summary>The consumer groups (<c>azure-native:eventhub:ConsumerGroup</c>) of the namespace.</summary>
    public IEnumerable<PulumiResourceNode> ConsumerGroups => GetResources("azure-native:eventhub:ConsumerGroup");
}

public static class PulumiEventHubsInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the Event Hubs resources before the program is emitted.</summary>
    public static IResourceBuilder<AzureEventHubsResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureEventHubsResource> builder, Action<PulumiEventHubsInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiEventHubsInfrastructure(infra, owner)));
    }
}
