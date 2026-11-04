using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Sheridan.Aspire.Pulumi.Azure.WebPubSub;

/// <summary>Pulumi equivalents of the Aspire.Hosting.Azure.WebPubSub extension methods.</summary>
public static class AzureWebPubSubExtensions
{
    /// <summary>Adds an Azure Web PubSub service provisioned by the Pulumi stack (the equivalent of AddAzureWebPubSub).</summary>
    public static IResourceBuilder<AzureWebPubSubResource> AddPulumiWebPubSub(this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new AzureWebPubSubResource(name)).ExcludeFromManifest();
    }

    /// <summary>Alias of <see cref="AddPulumiWebPubSub"/> following the <c>AddPulumiAzureX</c> naming used by the other Azure integrations.</summary>
    public static IResourceBuilder<AzureWebPubSubResource> AddPulumiAzureWebPubSub(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiWebPubSub(name);

    /// <summary>Overrides the SKU (default Free_F1) and capacity (default 1).</summary>
    public static IResourceBuilder<AzureWebPubSubResource> WithSku(this IResourceBuilder<AzureWebPubSubResource> builder, string sku, int capacity = 1)
    {
        ArgumentException.ThrowIfNullOrEmpty(sku);
        builder.Resource.Sku = sku;
        builder.Resource.Capacity = capacity;
        return builder;
    }

    /// <summary>Adds a hub to the service (<paramref name="hubName"/> defaults to <paramref name="name"/>).</summary>
    public static IResourceBuilder<AzureWebPubSubHubResource> AddHub(
        this IResourceBuilder<AzureWebPubSubResource> builder, string name, string? hubName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var resource = new AzureWebPubSubHubResource(name, hubName ?? name, builder.Resource);
        builder.Resource.Hubs.Add(resource);
        return builder.ApplicationBuilder.AddResource(resource).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>Adds an event handler to the hub. <paramref name="urlTemplate"/> may reference other resources' values.</summary>
    public static IResourceBuilder<AzureWebPubSubHubResource> AddEventHandler(
        this IResourceBuilder<AzureWebPubSubHubResource> builder, ReferenceExpression urlTemplate,
        string? userEventPattern = null, string[]? systemEvents = null)
    {
        ArgumentNullException.ThrowIfNull(urlTemplate);
        builder.Resource.EventHandlers.Add(new AzureWebPubSubEventHandler(urlTemplate, userEventPattern, systemEvents));
        return builder;
    }

    /// <summary>Adds an event handler with a literal URL template (e.g. <c>https://host/api/{event}</c>).</summary>
    public static IResourceBuilder<AzureWebPubSubHubResource> AddEventHandler(
        this IResourceBuilder<AzureWebPubSubHubResource> builder, string urlTemplate,
        string? userEventPattern = null, string[]? systemEvents = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(urlTemplate);
        return builder.AddEventHandler(ReferenceExpression.Create($"{urlTemplate}"), userEventPattern, systemEvents);
    }

    /// <summary>
    /// Replaces the default role assignment (Web PubSub Service Owner) for the compute environment identity hosting
    /// <paramref name="builder"/> with the given roles on the service. Mirrors <c>WithRoleAssignments</c>.
    /// </summary>
    public static IResourceBuilder<T> WithRoleAssignments<T>(
        this IResourceBuilder<T> builder, IResourceBuilder<AzureWebPubSubResource> target, params AzureWebPubSubRole[] roles)
        where T : IResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(roles);
        foreach (var role in roles)
            if (!Enum.IsDefined(role)) throw new ArgumentException($"'{role}' is not a valid Web PubSub role.", nameof(roles));
        return builder.WithAnnotation(new AzureWebPubSubRoleAnnotation(target.Resource, roles));
    }
}
