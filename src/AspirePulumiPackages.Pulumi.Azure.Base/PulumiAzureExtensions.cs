using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace AspirePulumiPackages.Pulumi.Azure.Base;

/// <summary>Shared <c>WithX</c> ergonomics for every Pulumi-provisioned Azure resource.</summary>
public static class PulumiAzureExtensions
{
    /// <summary>Overrides the Azure region (otherwise the stack's 'location' config value is used).</summary>
    public static IResourceBuilder<T> WithLocation<T>(this IResourceBuilder<T> builder, string location)
        where T : IPulumiAzureResource
    {
        builder.Resource.Location = location;
        return builder;
    }

    /// <summary>Binds the resource to a Pulumi stack (required once the model has more than one stack).</summary>
    public static IResourceBuilder<T> WithPulumiStack<T>(this IResourceBuilder<T> builder, IResourceBuilder<PulumiStackResource> stack)
        where T : IPulumiAzureResource
    {
        builder.Resource.Stack = stack.Resource;
        return builder;
    }

    /// <summary>Places the resource in an existing resource group instead of creating one.</summary>
    public static IResourceBuilder<T> WithExistingResourceGroup<T>(this IResourceBuilder<T> builder, string resourceGroupName)
        where T : PulumiAzureResource
    {
        builder.Resource.ExistingResourceGroup = resourceGroupName;
        return builder;
    }

    /// <summary>Adds a user-assigned managed identity provisioned by Pulumi (the equivalent of AddAzureUserAssignedIdentity).</summary>
    public static IResourceBuilder<PulumiAzureUserAssignedIdentityResource> AddPulumiAzureUserAssignedIdentity(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddResource(new PulumiAzureUserAssignedIdentityResource(name)).ExcludeFromManifest();
}
