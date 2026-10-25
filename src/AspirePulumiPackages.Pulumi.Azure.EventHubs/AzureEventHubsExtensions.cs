using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace AspirePulumiPackages.Pulumi.Azure.EventHubs;

/// <summary>Pulumi equivalents of the Aspire.Hosting.Azure.EventHubs extension methods.</summary>
public static class AzureEventHubsExtensions
{
    /// <summary>Adds an Azure Event Hubs namespace provisioned by the Pulumi stack (the equivalent of AddAzureEventHubs).</summary>
    public static IResourceBuilder<AzureEventHubsResource> AddPulumiEventHubs(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddResource(new AzureEventHubsResource(name)).ExcludeFromManifest();

    /// <summary>Alias of <see cref="AddPulumiEventHubs"/> following the <c>AddPulumiAzureX</c> naming used by the other Azure integrations.</summary>
    public static IResourceBuilder<AzureEventHubsResource> AddPulumiAzureEventHubs(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiEventHubs(name);

    /// <summary>Overrides the namespace SKU (default Standard).</summary>
    public static IResourceBuilder<AzureEventHubsResource> WithSku(this IResourceBuilder<AzureEventHubsResource> builder, string sku)
    {
        builder.Resource.Sku = sku;
        return builder;
    }

    /// <summary>Adds an event hub to the namespace (<paramref name="hubName"/> defaults to <paramref name="name"/>).</summary>
    public static IResourceBuilder<AzureEventHubResource> AddHub(
        this IResourceBuilder<AzureEventHubsResource> builder, string name, string? hubName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var resource = new AzureEventHubResource(name, hubName ?? name, builder.Resource);
        builder.Resource.Hubs.Add(resource);
        return builder.ApplicationBuilder.AddResource(resource).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>Adds an event hub to the namespace (the hub name equals <paramref name="name"/>).</summary>
    public static IResourceBuilder<AzureEventHubResource> AddEventHub(
        this IResourceBuilder<AzureEventHubsResource> builder, string name) => builder.AddHub(name);

    /// <summary>Adds a consumer group to the event hub (<paramref name="groupName"/> defaults to <paramref name="name"/>).</summary>
    public static IResourceBuilder<AzureEventHubConsumerGroupResource> AddConsumerGroup(
        this IResourceBuilder<AzureEventHubResource> builder, string name, string? groupName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var resource = new AzureEventHubConsumerGroupResource(name, groupName ?? name, builder.Resource);
        builder.Resource.ConsumerGroups.Add(resource);
        return builder.ApplicationBuilder.AddResource(resource).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>Configures the event hub (e.g. <c>PartitionCount</c>), as the official <c>WithProperties</c>.</summary>
    public static IResourceBuilder<AzureEventHubResource> WithProperties(
        this IResourceBuilder<AzureEventHubResource> builder, Action<AzureEventHubResource> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(builder.Resource);
        return builder;
    }

    /// <summary>
    /// Replaces the default role assignment (Azure Event Hubs Data Owner) for the compute environment identity hosting
    /// <paramref name="builder"/> with the given roles on the namespace. Mirrors <c>WithRoleAssignments</c>.
    /// </summary>
    public static IResourceBuilder<T> WithRoleAssignments<T>(
        this IResourceBuilder<T> builder, IResourceBuilder<AzureEventHubsResource> target, params AzureEventHubsRole[] roles)
        where T : IResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(roles);
        foreach (var role in roles)
            if (!Enum.IsDefined(role)) throw new ArgumentException($"'{role}' is not a valid Event Hubs role.", nameof(roles));
        return builder.WithAnnotation(new AzureEventHubsRoleAnnotation(target.Resource, roles));
    }
}
