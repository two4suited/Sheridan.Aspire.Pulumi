using System.Text.RegularExpressions;
using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Azure.Base;

namespace Aspire.Hosting;

/// <summary>Pulumi counterparts of the extension methods in the official <c>Aspire.Hosting.Azure.KeyVault</c> package.</summary>
public static partial class PulumiAzureKeyVaultResourceExtensions
{
    [GeneratedRegex("^[a-zA-Z0-9-]{1,127}$")]
    private static partial Regex SecretNameRegex();

    /// <summary>Adds an Azure Key Vault that the Pulumi stack provisions. Mirrors <c>AddAzureKeyVault</c>; the vault URI is the connection string.</summary>
    public static IResourceBuilder<AspirePulumiPackages.Pulumi.Azure.KeyVault.PulumiAzureKeyVaultResource> AddPulumiAzureKeyVault(
        this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new AspirePulumiPackages.Pulumi.Azure.KeyVault.PulumiAzureKeyVaultResource(name)).ExcludeFromManifest();
    }

    /// <summary>
    /// Uses an existing vault instead of creating one (nothing is provisioned for the vault itself, but secrets and role
    /// assignments are still applied to it). Mirrors <c>PublishAsExisting</c>.
    /// </summary>
    public static IResourceBuilder<AspirePulumiPackages.Pulumi.Azure.KeyVault.PulumiAzureKeyVaultResource> PublishAsExisting(
        this IResourceBuilder<AspirePulumiPackages.Pulumi.Azure.KeyVault.PulumiAzureKeyVaultResource> builder, string existingResourceName, string? resourceGroupName)
    {
        ArgumentException.ThrowIfNullOrEmpty(existingResourceName);
        ArgumentException.ThrowIfNullOrEmpty(resourceGroupName);
        builder.Resource.ExistingVaultName = existingResourceName;
        builder.Resource.ExistingResourceGroup = resourceGroupName;
        return builder;
    }

    /// <summary>Alias of <see cref="PublishAsExisting"/> (this resource has no local run mode). Mirrors <c>AsExisting</c>.</summary>
    public static IResourceBuilder<AspirePulumiPackages.Pulumi.Azure.KeyVault.PulumiAzureKeyVaultResource> AsExisting(
        this IResourceBuilder<AspirePulumiPackages.Pulumi.Azure.KeyVault.PulumiAzureKeyVaultResource> builder, string existingResourceName, string? resourceGroupName) =>
        builder.PublishAsExisting(existingResourceName, resourceGroupName);

    /// <summary>
    /// Adds a secret whose value comes from a parameter. The parameter becomes a secret Pulumi config entry
    /// (set it with <c>pulumi config set --secret &lt;parameter&gt;</c> or in the ESC environment).
    /// </summary>
    public static IResourceBuilder<AspirePulumiPackages.Pulumi.Azure.KeyVault.PulumiAzureKeyVaultResource> AddSecret(
        this IResourceBuilder<AspirePulumiPackages.Pulumi.Azure.KeyVault.PulumiAzureKeyVaultResource> builder, string name, IResourceBuilder<ParameterResource> parameterResource)
    {
        ArgumentNullException.ThrowIfNull(parameterResource);
        return builder.AddSecret(name, parameterResource.Resource);
    }

    /// <inheritdoc cref="AddSecret(IResourceBuilder{AspirePulumiPackages.Pulumi.Azure.KeyVault.PulumiAzureKeyVaultResource}, string, IResourceBuilder{ParameterResource})"/>
    public static IResourceBuilder<AspirePulumiPackages.Pulumi.Azure.KeyVault.PulumiAzureKeyVaultResource> AddSecret(
        this IResourceBuilder<AspirePulumiPackages.Pulumi.Azure.KeyVault.PulumiAzureKeyVaultResource> builder, string name, ParameterResource parameterResource)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(parameterResource);
        ValidateSecretName(name);
        if (builder.Resource.Secrets.Any(s => s.Name == name))
            throw new ArgumentException($"A secret named '{name}' was already added to '{builder.Resource.Name}'.", nameof(name));
        builder.Resource.Secrets.Add(new AspirePulumiPackages.Pulumi.Azure.KeyVault.PulumiAzureKeyVaultSecretResource(name, PulumiAzureProgramExtensions.ToPulumiId(parameterResource.Name)));
        return builder;
    }

    /// <summary>
    /// Gets a reference to a secret's URI (<c>https://vault/secrets/name/version</c>), usable with
    /// <c>WithEnvironment</c>. Unlike the official integration this is the URI, not a Key Vault secret reference resolved by the platform.
    /// </summary>
    public static IValueProvider GetSecret(
        this IResourceBuilder<AspirePulumiPackages.Pulumi.Azure.KeyVault.PulumiAzureKeyVaultResource> builder, string secretName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ValidateSecretName(secretName);
        return new SecretUriReference(builder.Resource, secretName);
    }

    /// <summary>
    /// Replaces the default role assignment (Key Vault Administrator) for the compute environment identity hosting
    /// <paramref name="builder"/> with the given roles on the vault. Mirrors <c>WithRoleAssignments</c>.
    /// </summary>
    public static IResourceBuilder<T> WithRoleAssignments<T>(
        this IResourceBuilder<T> builder, IResourceBuilder<AspirePulumiPackages.Pulumi.Azure.KeyVault.PulumiAzureKeyVaultResource> target,
        params AspirePulumiPackages.Pulumi.Azure.KeyVault.AzureKeyVaultRole[] roles)
        where T : IResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(roles);
        foreach (var role in roles)
        {
            if (!Enum.IsDefined(role)) throw new ArgumentException($"'{role}' is not a valid Key Vault role.", nameof(roles));
        }
        return builder.WithAnnotation(new AspirePulumiPackages.Pulumi.Azure.KeyVault.AzureKeyVaultRoleAnnotation(target.Resource, roles));
    }

    /// <summary>
    /// Grants the principal running the Pulumi deployment roles on the vault (e.g. Secrets Officer), so Pulumi can write
    /// secrets to the RBAC-authorized vault; secrets are created after the assignment. Pulumi-specific (no official equivalent).
    /// </summary>
    public static IResourceBuilder<AspirePulumiPackages.Pulumi.Azure.KeyVault.PulumiAzureKeyVaultResource> WithDeployerRoleAssignments(
        this IResourceBuilder<AspirePulumiPackages.Pulumi.Azure.KeyVault.PulumiAzureKeyVaultResource> builder,
        params AspirePulumiPackages.Pulumi.Azure.KeyVault.AzureKeyVaultRole[] roles)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(roles);
        builder.Resource.DeployerRoles.AddRange(roles);
        return builder;
    }

    private static void ValidateSecretName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!SecretNameRegex().IsMatch(name))
            throw new ArgumentException("Secret names must be 1-127 characters of letters, digits and hyphens.", nameof(name));
    }

    private sealed class SecretUriReference(AspirePulumiPackages.Pulumi.Azure.KeyVault.PulumiAzureKeyVaultResource vault, string secretName)
        : IValueProvider, IManifestExpressionProvider
    {
        public string ValueExpression => vault.SecretUri(secretName).ValueExpression;

        public ValueTask<string?> GetValueAsync(CancellationToken cancellationToken = default) =>
            vault.SecretUri(secretName).GetValueAsync(cancellationToken);
    }
}
