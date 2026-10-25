using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.KeyVault;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi Key Vault. Port of the Azure.Provisioning
/// KeyVault overlay (root: <c>KeyVaultService</c>). Properties use azure-native names, e.g.
/// <c>KeyVaultService.Set("properties.enableSoftDelete", true)</c>.
/// </summary>
public sealed class PulumiKeyVaultInfrastructure(PulumiInfrastructure inner, PulumiAzureKeyVaultResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:keyvault:Vault</c>. Throws for vaults declared with PublishAsExisting.</summary>
    public PulumiResourceNode KeyVaultService => owner.ExistingVaultName is null
        ? GetResource(owner.Id)
        : throw new InvalidOperationException($"'{owner.Name}' is an existing vault; there is no Vault resource to configure.");

    /// <summary>The secret resources (<c>azure-native:keyvault:Secret</c>) of this vault.</summary>
    public IEnumerable<PulumiResourceNode> Secrets =>
        GetResources("azure-native:keyvault:Secret").Where(r => r.Id.StartsWith($"{owner.Id}Secret", StringComparison.Ordinal));
}

public static class PulumiKeyVaultInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the Key Vault resources before the program is emitted.</summary>
    public static IResourceBuilder<PulumiAzureKeyVaultResource> ConfigureInfrastructure(
        this IResourceBuilder<PulumiAzureKeyVaultResource> builder, Action<PulumiKeyVaultInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiKeyVaultInfrastructure(infra, owner)));
    }
}
