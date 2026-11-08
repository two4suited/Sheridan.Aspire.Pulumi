using Sheridan.Aspire.Pulumi;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Sheridan.Aspire.Pulumi.Azure.Base;

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

    /// <summary>Places the resource in a Pulumi-provisioned resource group (see <see cref="AddPulumiAzureResourceGroup"/>) instead of creating its own.</summary>
    public static IResourceBuilder<T> WithResourceGroup<T>(this IResourceBuilder<T> builder, IResourceBuilder<PulumiAzureResourceGroupResource> resourceGroup)
        where T : PulumiAzureResource
    {
        builder.Resource.ResourceGroup = resourceGroup.Resource;
        return builder;
    }

    /// <summary>
    /// Pins the Azure name of one of the resource's Azure resources instead of composing it from the app, resource and stack names.
    /// The name is validated against the type's naming rules immediately and throws an <see cref="ArgumentException"/> if invalid.
    /// <paramref name="suffix"/> selects among several resources of the same kind (e.g. "kv"); null targets the primary one.
    /// </summary>
    public static IResourceBuilder<T> WithAzureName<T>(this IResourceBuilder<T> builder, AzureNameKind kind, string name, string? suffix = null)
        where T : IPulumiAzureNamed
    {
        builder.Resource.NameOverrides[(kind, suffix)] = AzureNaming.ValidateOverride(kind, name);
        return builder;
    }

    /// <summary>Adds a standalone Azure resource group provisioned by Pulumi, for grouping several resources together with <see cref="WithResourceGroup{T}"/>.</summary>
    public static IResourceBuilder<PulumiAzureResourceGroupResource> AddPulumiAzureResourceGroup(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddResource(new PulumiAzureResourceGroupResource(name)).ExcludeFromManifest().HideInRunMode();

    /// <summary>Adds a user-assigned managed identity provisioned by Pulumi (the equivalent of AddAzureUserAssignedIdentity).</summary>
    public static IResourceBuilder<PulumiAzureUserAssignedIdentityResource> AddPulumiAzureUserAssignedIdentity(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddResource(new PulumiAzureUserAssignedIdentityResource(name)).ExcludeFromManifest();
}
