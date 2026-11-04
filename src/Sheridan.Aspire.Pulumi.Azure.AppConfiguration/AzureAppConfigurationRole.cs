namespace Sheridan.Aspire.Pulumi.Azure.AppConfiguration;

/// <summary>App Configuration data-plane built-in roles (mirrors the official <c>AzureAppConfigurationRole</c>).</summary>
public enum AzureAppConfigurationRole
{
    AppConfigurationDataOwner,
    AppConfigurationDataReader,
}

internal static class AzureAppConfigurationRoles
{
    public static string RoleDefinitionId(AzureAppConfigurationRole role) => role switch
    {
        AzureAppConfigurationRole.AppConfigurationDataOwner => "5ae67dd6-50cb-40e7-96ff-dc2bfa4b606b",
        AzureAppConfigurationRole.AppConfigurationDataReader => "516239f1-63e1-4d78-a4de-a74fb236a071",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}
