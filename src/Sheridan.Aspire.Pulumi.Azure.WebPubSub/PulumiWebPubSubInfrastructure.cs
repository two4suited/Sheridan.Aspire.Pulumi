using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Provisioning;

namespace Sheridan.Aspire.Pulumi.Azure.WebPubSub;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi Web PubSub service. Port of the Azure.Provisioning
/// WebPubSub overlay (root: <c>WebPubSubService</c>). Properties use azure-native names,
/// e.g. <c>WebPubSubService.Set("tls.clientCertEnabled", true)</c>.
/// </summary>
public sealed class PulumiWebPubSubInfrastructure(PulumiInfrastructure inner, AzureWebPubSubResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:webpubsub:WebPubSub</c>.</summary>
    public PulumiResourceNode WebPubSubService => GetResource(owner.Id);

    /// <summary>The hub resources (<c>azure-native:webpubsub:WebPubSubHub</c>) of this service.</summary>
    public IEnumerable<PulumiResourceNode> Hubs => owner.Hubs.Select(h => GetResource($"{owner.Id}{h.Id}"));
}

public static class PulumiWebPubSubInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the Web PubSub resources before the program is emitted.</summary>
    public static IResourceBuilder<AzureWebPubSubResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureWebPubSubResource> builder, Action<PulumiWebPubSubInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiWebPubSubInfrastructure(infra, owner)));
    }
}
