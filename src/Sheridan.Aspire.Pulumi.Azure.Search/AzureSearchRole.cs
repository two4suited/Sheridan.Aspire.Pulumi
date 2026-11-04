namespace Sheridan.Aspire.Pulumi.Azure.Search;

/// <summary>Azure AI Search data-plane built-in roles (mirrors the official <c>AzureSearchRole</c>).</summary>
public enum AzureSearchRole
{
    SearchIndexDataContributor,
    SearchIndexDataReader,
    SearchServiceContributor,
}

internal static class AzureSearchRoles
{
    public static string RoleDefinitionId(AzureSearchRole role) => role switch
    {
        AzureSearchRole.SearchIndexDataContributor => "8ebe5a00-799e-43f5-93ac-243d3dce84a7",
        AzureSearchRole.SearchIndexDataReader => "1407120a-92aa-4202-b7e9-c0e197c71c8f",
        AzureSearchRole.SearchServiceContributor => "7ca78c08-252a-4471-8644-bb5ff32d4ba0",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}
