using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi;

namespace AspirePulumiPackages.Pulumi.Entra;

public static class AzureAdAppRegistrationExtensions
{
    /// <summary>Adds an Azure AD (Entra ID) app registration and service principal that the Pulumi stack provisions.</summary>
    public static IResourceBuilder<AzureAdAppRegistrationResource> AddPulumiAzureAdAppRegistration(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddResource(new AzureAdAppRegistrationResource(name)).ExcludeFromManifest();

    public static IResourceBuilder<AzureAdAppRegistrationResource> WithDisplayName(
        this IResourceBuilder<AzureAdAppRegistrationResource> builder, string displayName)
    {
        builder.Resource.DisplayName = displayName;
        return builder;
    }

    public static IResourceBuilder<AzureAdAppRegistrationResource> WithPulumiStack(
        this IResourceBuilder<AzureAdAppRegistrationResource> builder, IResourceBuilder<PulumiStackResource> stack)
    {
        builder.Resource.Stack = stack.Resource;
        return builder;
    }
}
