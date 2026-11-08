using Aspire.Hosting.ApplicationModel;

namespace Sheridan.Aspire.Pulumi.Azure.Storage;

/// <summary>Azure Storage built-in roles (mirrors the official integration's <c>AzureStorageRole</c>, which is internal there).</summary>
public enum AzureStorageRole
{
    ClassicStorageAccountContributor,
    ClassicStorageAccountKeyOperatorServiceRole,
    StorageAccountBackupContributor,
    StorageAccountContributor,
    StorageAccountKeyOperatorServiceRole,
    StorageBlobDataContributor,
    StorageBlobDataOwner,
    StorageBlobDataReader,
    StorageBlobDelegator,
    StorageFileDataPrivilegedContributor,
    StorageFileDataPrivilegedReader,
    StorageFileDataSmbShareContributor,
    StorageFileDataSmbShareReader,
    StorageFileDataSmbShareElevatedContributor,
    StorageQueueDataContributor,
    StorageQueueDataReader,
    StorageQueueDataMessageSender,
    StorageQueueDataMessageProcessor,
    StorageTableDataContributor,
    StorageTableDataReader,
}

internal static class AzureStorageRoles
{
    public static string RoleDefinitionId(AzureStorageRole role) => role switch
    {
        AzureStorageRole.ClassicStorageAccountContributor => "86e8f5dc-a6e9-4c67-9d15-de283e8eac25",
        AzureStorageRole.ClassicStorageAccountKeyOperatorServiceRole => "985d6b00-f706-48f5-a6fe-d0ca12fb668d",
        AzureStorageRole.StorageAccountBackupContributor => "e5e2a7ff-d759-4cd2-bb51-3152d37e2eb1",
        AzureStorageRole.StorageAccountContributor => "17d1049b-9a84-46fb-8f53-869881c3d3ab",
        AzureStorageRole.StorageAccountKeyOperatorServiceRole => "81a9662b-bebf-436f-a333-f67b29880f12",
        AzureStorageRole.StorageBlobDataContributor => "ba92f5b4-2d11-453d-a403-e96b0029c9fe",
        AzureStorageRole.StorageBlobDataOwner => "b7e6dc6d-f1e8-4753-8033-0f276bb0955b",
        AzureStorageRole.StorageBlobDataReader => "2a2b9908-6ea1-4ae2-8e65-a410df84e7d1",
        AzureStorageRole.StorageBlobDelegator => "db58b8e5-c6ad-4a2a-8342-4190687cbf4a",
        AzureStorageRole.StorageFileDataPrivilegedContributor => "69566ab7-960f-475b-8e7c-b3118f30c6bd",
        AzureStorageRole.StorageFileDataPrivilegedReader => "b8eda974-7b85-4f76-af95-65846b26df6d",
        AzureStorageRole.StorageFileDataSmbShareContributor => "0c867c2a-1d8c-454a-a3db-ab2ea1bdc8bb",
        AzureStorageRole.StorageFileDataSmbShareReader => "aba4ae5f-2193-4029-9191-0cb91df5e314",
        AzureStorageRole.StorageFileDataSmbShareElevatedContributor => "a7264617-510b-434b-a828-9731dc254ea7",
        AzureStorageRole.StorageQueueDataContributor => "974c5e8b-45b9-4653-ba55-5f855dd0fb88",
        AzureStorageRole.StorageQueueDataReader => "19e7f393-937e-4f77-808e-94535e297925",
        AzureStorageRole.StorageQueueDataMessageSender => "c6a89b2d-59bc-44d0-9896-0f6e12d7b80a",
        AzureStorageRole.StorageQueueDataMessageProcessor => "8a0f0c08-91a1-4084-bc3d-661d67233fed",
        AzureStorageRole.StorageTableDataContributor => "0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3",
        AzureStorageRole.StorageTableDataReader => "76199698-9eea-4c19-bc75-cec21354c6b6",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}

/// <summary>Roles the annotated resource's identity should get on a storage account (replaces the default Blob/Queue/Table Data Contributor).</summary>
internal sealed class AzureStorageRoleAnnotation(AzureStorageResource storage, AzureStorageRole[] roles) : IResourceAnnotation
{
    public AzureStorageResource Storage { get; } = storage;
    public AzureStorageRole[] Roles { get; } = roles;
}
