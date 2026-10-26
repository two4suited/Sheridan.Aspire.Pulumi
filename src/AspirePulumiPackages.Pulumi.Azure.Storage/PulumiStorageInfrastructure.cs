using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.Storage;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi Storage account. Port of the Azure.Provisioning
/// Storage overlay (root: <c>StorageAccount</c>). Properties use azure-native names, e.g.
/// <c>StorageAccount.Set("allowBlobPublicAccess", false)</c>.
/// </summary>
public sealed class PulumiStorageInfrastructure(PulumiInfrastructure inner, AzureStorageResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:storage:StorageAccount</c> resource.</summary>
    public PulumiResourceNode StorageAccount => GetResource(owner.Id);
}

public static class PulumiStorageInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the Storage resources before the program is emitted.</summary>
    public static IResourceBuilder<AzureStorageResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureStorageResource> builder, Action<PulumiStorageInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiStorageInfrastructure(infra, owner)));
    }
}
