namespace AspirePulumiPackages.Pulumi.Azure.SignalR;

/// <summary>Azure SignalR built-in roles (mirrors the official <c>AzureSignalRRole</c>).</summary>
public enum AzureSignalRRole
{
    SignalRContributor,
    SignalRAppServer,
    SignalRRestApiOwner,
}

/// <summary>Whether the service runs in Default (hub server) or Serverless mode (mirrors <c>AzureSignalRServiceMode</c>).</summary>
public enum AzureSignalRServiceMode
{
    Default,
    Serverless,
}

internal static class AzureSignalRRoles
{
    public static string RoleDefinitionId(AzureSignalRRole role) => role switch
    {
        AzureSignalRRole.SignalRContributor => "8cf5e20a-e4b2-4e9d-b3a1-5ceb692c2761",
        AzureSignalRRole.SignalRAppServer => "420fcaa2-552c-430f-98ca-3264be4806c7",
        AzureSignalRRole.SignalRRestApiOwner => "fd53cd77-2268-407a-8f46-7e7863d0f521",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}
