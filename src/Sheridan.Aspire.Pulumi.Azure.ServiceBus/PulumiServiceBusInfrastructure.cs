using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Provisioning;

namespace Sheridan.Aspire.Pulumi.Azure.ServiceBus;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi Service Bus namespace. Port of the Azure.Provisioning
/// ServiceBus overlay (root: <c>ServiceBusNamespace</c>). Properties use azure-native names, e.g.
/// <c>ServiceBusNamespace.Set("minimumTlsVersion", "1.2")</c>.
/// </summary>
public sealed class PulumiServiceBusInfrastructure(PulumiInfrastructure inner, AzureServiceBusResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:servicebus:Namespace</c> resource. Throws for namespaces declared with AsExisting/PublishAsExisting.</summary>
    public PulumiResourceNode ServiceBusNamespace => owner.ExistingNamespaceName is null
        ? GetResource(owner.Id)
        : throw new InvalidOperationException($"'{owner.Name}' is an existing namespace; there is no Namespace resource to configure.");
}

public static class PulumiServiceBusInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the Service Bus resources before the program is emitted.</summary>
    public static IResourceBuilder<AzureServiceBusResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureServiceBusResource> builder, Action<PulumiServiceBusInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiServiceBusInfrastructure(infra, owner)));
    }
}
