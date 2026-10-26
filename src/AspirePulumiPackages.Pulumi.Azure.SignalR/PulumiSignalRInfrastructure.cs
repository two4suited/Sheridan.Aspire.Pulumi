using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.SignalR;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi SignalR service. Port of the Azure.Provisioning
/// SignalR overlay (root: <c>SignalRService</c>). Properties use azure-native names, e.g.
/// <c>SignalRService.Set("cors.allowedOrigins", new[] { "https://example.com" })</c>.
/// </summary>
public sealed class PulumiSignalRInfrastructure(PulumiInfrastructure inner, AzureSignalRResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:signalrservice:SignalR</c> resource.</summary>
    public PulumiResourceNode SignalRService => GetResource(owner.Id);
}
