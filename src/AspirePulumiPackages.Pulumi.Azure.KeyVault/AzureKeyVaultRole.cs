namespace AspirePulumiPackages.Pulumi.Azure.KeyVault;

/// <summary>Key Vault built-in roles (mirrors the official <c>AzureKeyVaultRole</c> / <c>KeyVaultBuiltInRole</c>).</summary>
public enum AzureKeyVaultRole
{
    KeyVaultAdministrator,
    KeyVaultCertificatesOfficer,
    KeyVaultCertificateUser,
    KeyVaultContributor,
    KeyVaultCryptoOfficer,
    KeyVaultCryptoServiceEncryptionUser,
    KeyVaultCryptoServiceReleaseUser,
    KeyVaultCryptoUser,
    KeyVaultDataAccessAdministrator,
    KeyVaultPurgeOperator,
    KeyVaultReader,
    KeyVaultSecretsOfficer,
    KeyVaultSecretsUser,
}

internal static class AzureKeyVaultRoles
{
    public static string RoleDefinitionId(AzureKeyVaultRole role) => role switch
    {
        AzureKeyVaultRole.KeyVaultAdministrator => "00482a5a-887f-4fb3-b363-3b7fe8e74483",
        AzureKeyVaultRole.KeyVaultCertificatesOfficer => "a4417e6f-fecd-4de8-b567-7b0420556985",
        AzureKeyVaultRole.KeyVaultCertificateUser => "db79e9a7-68ee-4b58-9aeb-b90e7c24fcba",
        AzureKeyVaultRole.KeyVaultContributor => "f25e0fa2-a7c8-4377-a976-54943a77a395",
        AzureKeyVaultRole.KeyVaultCryptoOfficer => "14b46e9e-c2b7-41b4-b07b-48a6ebf60603",
        AzureKeyVaultRole.KeyVaultCryptoServiceEncryptionUser => "e147488a-f6f5-4113-8e2d-b22465e65bf6",
        AzureKeyVaultRole.KeyVaultCryptoServiceReleaseUser => "08bbd89e-9f13-488c-ac41-acfcb10c90ab",
        AzureKeyVaultRole.KeyVaultCryptoUser => "12338af0-0e69-4776-bea7-57ae8d297424",
        AzureKeyVaultRole.KeyVaultDataAccessAdministrator => "8b54135c-b56d-4d72-a534-26097cfdc8d8",
        AzureKeyVaultRole.KeyVaultPurgeOperator => "a68e7c17-0ab2-4c09-9a58-125dae29766a",
        AzureKeyVaultRole.KeyVaultReader => "21090545-7ca7-4776-b22c-e363652d74d2",
        AzureKeyVaultRole.KeyVaultSecretsOfficer => "b86a8fe4-44ce-4948-aee5-eccb2c155cd7",
        AzureKeyVaultRole.KeyVaultSecretsUser => "4633458b-17de-408a-b874-0445c86b69e6",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}
