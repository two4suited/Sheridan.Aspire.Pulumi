using Sheridan.Aspire.Pulumi.Azure.Base;

namespace Sheridan.Aspire.Pulumi.Azure.ContainerRegistry;

/// <summary>Built-in roles assignable on a container registry (Pulumi counterpart of the official <c>AzureContainerRegistryRole</c>).</summary>
public enum AzureContainerRegistryRole
{
    AcrDelete,
    AcrImageSigner,
    AcrPull,
    AcrPush,
    AcrQuarantineReader,
    AcrQuarantineWriter,
    Contributor,
    Owner,
    Reader,
}

internal static class AzureContainerRegistryRoles
{
    public static string RoleDefinitionId(this AzureContainerRegistryRole role) => role switch
    {
        AzureContainerRegistryRole.AcrDelete => "c2f4ef07-c644-48eb-af81-4b1b4947fb11",
        AzureContainerRegistryRole.AcrImageSigner => "6cef56e8-d556-48e5-a04f-b8e64114680f",
        AzureContainerRegistryRole.AcrPull => AzureRoleDefinitions.AcrPull,
        AzureContainerRegistryRole.AcrPush => AzureRoleDefinitions.AcrPush,
        AzureContainerRegistryRole.AcrQuarantineReader => "cdda3590-29a3-44f6-95f2-9f980659eb04",
        AzureContainerRegistryRole.AcrQuarantineWriter => "c8d4ff99-41c3-41a8-9f60-21dfdad59608",
        AzureContainerRegistryRole.Contributor => AzureRoleDefinitions.Contributor,
        AzureContainerRegistryRole.Owner => AzureRoleDefinitions.Owner,
        AzureContainerRegistryRole.Reader => AzureRoleDefinitions.Reader,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}
