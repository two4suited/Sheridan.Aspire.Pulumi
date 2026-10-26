using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.AppConfiguration;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi App Configuration store. Port of the Azure.Provisioning
/// AppConfiguration overlay (root: <c>AppConfigurationStore</c>). Properties use azure-native names, e.g.
/// <c>AppConfigurationStore.Set("softDeleteRetentionInDays", 7)</c>.
/// </summary>
public sealed class PulumiAppConfigurationInfrastructure(PulumiInfrastructure inner, AzureAppConfigurationResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:appconfiguration:ConfigurationStore</c>. Throws for stores declared with PublishAsExisting.</summary>
    public PulumiResourceNode AppConfigurationStore => owner.ExistingStoreName is null
        ? GetResource(owner.Id)
        : throw new InvalidOperationException($"'{owner.Name}' is an existing store; there is no ConfigurationStore resource to configure.");
}

public static class PulumiAppConfigurationInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the App Configuration resources before the program is emitted.</summary>
    public static IResourceBuilder<AzureAppConfigurationResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureAppConfigurationResource> builder, Action<PulumiAppConfigurationInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiAppConfigurationInfrastructure(infra, owner)));
    }
}
