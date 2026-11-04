namespace Sheridan.Aspire.Pulumi.Azure.WebPubSub;

/// <summary>Web PubSub data-plane built-in roles (mirrors the official <c>AzureWebPubSubRole</c>).</summary>
public enum AzureWebPubSubRole
{
    WebPubSubServiceOwner,
    WebPubSubServiceReader,
}

internal static class AzureWebPubSubRoles
{
    public static string RoleDefinitionId(AzureWebPubSubRole role) => role switch
    {
        AzureWebPubSubRole.WebPubSubServiceOwner => "12cf5a90-567b-43ae-8102-96cf46c7d9b4",
        AzureWebPubSubRole.WebPubSubServiceReader => "bfb1c7d2-fb1a-466b-b2ba-aee63b92deaf",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}
