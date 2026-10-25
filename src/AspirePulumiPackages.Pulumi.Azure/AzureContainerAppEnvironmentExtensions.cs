using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi;

namespace AspirePulumiPackages.Pulumi.Azure;

public static class AzureContainerAppEnvironmentExtensions
{
    /// <summary>Adds an Azure Container Apps environment that the Pulumi stack provisions.</summary>
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> AddPulumiAzureContainerAppEnvironment(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddResource(new AzureContainerAppEnvironmentResource(name)).ExcludeFromManifest();

    public static IResourceBuilder<AzureContainerAppEnvironmentResource> WithLocation(
        this IResourceBuilder<AzureContainerAppEnvironmentResource> builder, string location)
    {
        builder.Resource.Location = location;
        return builder;
    }

    public static IResourceBuilder<AzureContainerAppEnvironmentResource> WithPulumiStack(
        this IResourceBuilder<AzureContainerAppEnvironmentResource> builder, IResourceBuilder<PulumiStackResource> stack)
    {
        builder.Resource.Stack = stack.Resource;
        return builder;
    }
}
