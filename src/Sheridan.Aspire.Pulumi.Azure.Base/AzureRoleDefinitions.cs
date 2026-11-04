namespace Sheridan.Aspire.Pulumi.Azure.Base;

/// <summary>Well-known Azure built-in role definition ids (mirrors the role constants used by the Aspire.Hosting.Azure.* packages).</summary>
public static class AzureRoleDefinitions
{
    public const string Owner = "8e3af657-a8ff-443c-a75c-2fe8c4bcb635";
    public const string Contributor = "b24988ac-6180-42a0-ab88-20f7382dd24c";
    public const string Reader = "acdd72a7-3385-48ef-bd42-f606fba81ae7";
    public const string AcrPull = "7f951dda-4ed3-4680-a7ca-43fe172d538d";
    public const string AcrPush = "8311e382-0749-4cb8-b61a-304f252e45ec";
    public const string KeyVaultSecretsUser = "4633458b-17de-408a-b874-0445c86b69e6";
    public const string KeyVaultSecretsOfficer = "b86a8fe4-44ce-4948-aee5-eccb2c155cd7";
    public const string KeyVaultAdministrator = "00482a5a-887f-4fb3-b363-3b7fe8e74483";
    public const string StorageBlobDataContributor = "ba92f5b4-2d11-453d-a403-e96b0029c9fe";
}
