namespace AspirePulumiPackages.Pulumi.Azure.Foundry;

/// <summary>Microsoft Foundry (Cognitive Services) built-in roles (mirrors the official <c>FoundryRole</c>).</summary>
public enum FoundryRole
{
    CognitiveServicesOpenAIContributor,
    CognitiveServicesOpenAIUser,
    CognitiveServicesUser,
}

internal static class FoundryRoles
{
    public static string RoleDefinitionId(FoundryRole role) => role switch
    {
        FoundryRole.CognitiveServicesOpenAIContributor => "a001fd3d-188f-4b5d-821b-7da978bf7442",
        FoundryRole.CognitiveServicesOpenAIUser => "5e0bd9bd-7b93-4f28-af87-19fc36ad61bd",
        FoundryRole.CognitiveServicesUser => "a97b65f3-24c7-4388-baec-2e87135dc908",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}
