namespace Sheridan.Aspire.Pulumi.Azure.CognitiveServices;

/// <summary>Cognitive Services / Azure OpenAI built-in roles (mirrors the official <c>AzureOpenAIRole</c>).</summary>
public enum AzureOpenAIRole
{
    CognitiveServicesOpenAIContributor,
    CognitiveServicesOpenAIUser,
    CognitiveServicesUser,
}

internal static class AzureOpenAIRoles
{
    public static string RoleDefinitionId(AzureOpenAIRole role) => role switch
    {
        AzureOpenAIRole.CognitiveServicesOpenAIContributor => "a001fd3d-188f-4b5d-821b-7da978bf7442",
        AzureOpenAIRole.CognitiveServicesOpenAIUser => "5e0bd9bd-7b93-4f28-af87-19fc36ad61bd",
        AzureOpenAIRole.CognitiveServicesUser => "a97b65f3-24c7-4388-baec-2e87135dc908",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}
