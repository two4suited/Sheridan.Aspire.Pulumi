using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace AspirePulumiPackages.Pulumi.Azure.Search;

/// <summary>Pulumi equivalents of the Aspire.Hosting.Azure.Search extension methods.</summary>
public static class AzureSearchExtensions
{
    /// <summary>Adds an Azure AI Search service provisioned by the Pulumi stack (the equivalent of AddAzureSearch).</summary>
    public static IResourceBuilder<AzureSearchResource> AddPulumiSearch(this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new AzureSearchResource(name)).ExcludeFromManifest();
    }

    /// <summary>Alias of <see cref="AddPulumiSearch"/> following the <c>AddPulumiAzureX</c> naming used by the other Azure integrations.</summary>
    public static IResourceBuilder<AzureSearchResource> AddPulumiAzureSearch(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiSearch(name);

    /// <summary>Overrides the service SKU (default basic, as in the official integration), e.g. <c>"standard"</c>.</summary>
    public static IResourceBuilder<AzureSearchResource> WithSku(this IResourceBuilder<AzureSearchResource> builder, string sku)
    {
        ArgumentException.ThrowIfNullOrEmpty(sku);
        builder.Resource.Sku = sku;
        return builder;
    }

    /// <summary>
    /// Replaces the default role assignments (Search Index Data Contributor and Search Service Contributor) for the compute
    /// environment identity hosting <paramref name="builder"/> with the given roles on the service. Mirrors <c>WithRoleAssignments</c>.
    /// </summary>
    public static IResourceBuilder<T> WithRoleAssignments<T>(
        this IResourceBuilder<T> builder, IResourceBuilder<AzureSearchResource> target, params AzureSearchRole[] roles)
        where T : IResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(roles);
        foreach (var role in roles)
            if (!Enum.IsDefined(role)) throw new ArgumentException($"'{role}' is not a valid Search role.", nameof(roles));
        return builder.WithAnnotation(new AzureSearchRoleAnnotation(target.Resource, roles));
    }
}
