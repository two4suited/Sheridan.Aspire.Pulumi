namespace AspirePulumiPackages.Pulumi.Azure.EntraId;

/// <summary>Specifies which Microsoft accounts can sign in to an Entra ID application (mirrors the official enum).</summary>
public enum EntraIdSignInAudience
{
    /// <summary>Only work or school accounts in the app's home tenant. The default.</summary>
    AzureADMyOrg = 0,

    /// <summary>Work or school accounts in any Microsoft Entra tenant.</summary>
    AzureADMultipleOrgs,

    /// <summary>Work or school accounts in any Entra tenant, and personal Microsoft accounts.</summary>
    AzureADandPersonalMicrosoftAccount,

    /// <summary>Only personal Microsoft accounts.</summary>
    PersonalMicrosoftAccount,
}
