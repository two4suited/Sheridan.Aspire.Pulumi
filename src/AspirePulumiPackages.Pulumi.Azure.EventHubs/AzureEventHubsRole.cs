namespace AspirePulumiPackages.Pulumi.Azure.EventHubs;

/// <summary>Event Hubs data-plane built-in roles (mirrors the official <c>AzureEventHubsRole</c>).</summary>
public enum AzureEventHubsRole
{
    AzureEventHubsDataOwner,
    AzureEventHubsDataReceiver,
    AzureEventHubsDataSender,
}

internal static class AzureEventHubsRoles
{
    public static string RoleDefinitionId(AzureEventHubsRole role) => role switch
    {
        AzureEventHubsRole.AzureEventHubsDataOwner => "f526a384-b230-433a-b45c-95f59c4a2dec",
        AzureEventHubsRole.AzureEventHubsDataReceiver => "a638d3c7-ab3a-418d-83e6-5f17a39d4fde",
        AzureEventHubsRole.AzureEventHubsDataSender => "2b629674-e913-4c01-ae53-ef4638d8f975",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}
