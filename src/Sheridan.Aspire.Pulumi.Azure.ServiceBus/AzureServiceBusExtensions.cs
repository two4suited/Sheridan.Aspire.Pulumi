using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Sheridan.Aspire.Pulumi.Azure.ServiceBus;

/// <summary>Pulumi equivalents of the Aspire.Hosting.Azure.ServiceBus extension methods.</summary>
public static class AzureServiceBusExtensions
{
    /// <summary>Adds an Azure Service Bus namespace provisioned by the Pulumi stack (the equivalent of AddAzureServiceBus).</summary>
    public static IResourceBuilder<AzureServiceBusResource> AddPulumiServiceBus(this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new AzureServiceBusResource(name)).ExcludeFromManifest();
    }

    /// <summary>Alias of <see cref="AddPulumiServiceBus"/> following the <c>AddPulumiAzureX</c> naming used by the other Azure integrations.</summary>
    public static IResourceBuilder<AzureServiceBusResource> AddPulumiAzureServiceBus(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiServiceBus(name);

    /// <summary>
    /// Uses an existing namespace instead of creating one: it is looked up by name and resource group, and the queues, topics,
    /// subscriptions and role assignments are still applied to it. Mirrors <c>PublishAsExisting</c>.
    /// </summary>
    /// <param name="existingResourceName">Name of the existing Service Bus namespace.</param>
    /// <param name="resourceGroupName">Resource group of the namespace; when omitted, the resource group configured for this resource (<c>WithResourceGroup</c>/<c>WithExistingResourceGroup</c>) is used.</param>
    public static IResourceBuilder<AzureServiceBusResource> PublishAsExisting(
        this IResourceBuilder<AzureServiceBusResource> builder, string existingResourceName, string? resourceGroupName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(existingResourceName);
        if (resourceGroupName is not null) ArgumentException.ThrowIfNullOrEmpty(resourceGroupName);
        builder.Resource.ExistingNamespaceName = existingResourceName;
        builder.Resource.ExistingNamespaceResourceGroup = resourceGroupName;
        // Also marks a resource group as configured so none is created just for the lookup.
        if (resourceGroupName is not null) builder.Resource.ExistingResourceGroup = resourceGroupName;
        return builder;
    }

    /// <summary>Alias of <see cref="PublishAsExisting"/>. For local runs, add <c>RunAsExisting</c> or <c>RunAsEmulator</c>. Mirrors <c>AsExisting</c>.</summary>
    public static IResourceBuilder<AzureServiceBusResource> AsExisting(
        this IResourceBuilder<AzureServiceBusResource> builder, string existingResourceName, string? resourceGroupName = null) =>
        builder.PublishAsExisting(existingResourceName, resourceGroupName);

    /// <summary>Overrides the namespace SKU (default Standard). Pulumi-specific; upstream exposes this as a Bicep parameter.</summary>
    public static IResourceBuilder<AzureServiceBusResource> WithSku(this IResourceBuilder<AzureServiceBusResource> builder, string sku)
    {
        ArgumentException.ThrowIfNullOrEmpty(sku);
        builder.Resource.Sku = sku;
        return builder;
    }

    /// <summary>Adds a queue (<paramref name="queueName"/> defaults to <paramref name="name"/>).</summary>
    public static IResourceBuilder<AzureServiceBusQueueResource> AddServiceBusQueue(
        this IResourceBuilder<AzureServiceBusResource> builder, string name, string? queueName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var queue = new AzureServiceBusQueueResource(name, queueName ?? name, builder.Resource);
        builder.Resource.Queues.Add(queue);
        return builder.ApplicationBuilder.AddResource(queue).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>Adds a queue named <paramref name="name"/>.</summary>
    public static IResourceBuilder<AzureServiceBusResource> AddQueue(this IResourceBuilder<AzureServiceBusResource> builder, string name)
    {
        builder.AddServiceBusQueue(name);
        return builder;
    }

    /// <summary>Configures the queue's properties.</summary>
    public static IResourceBuilder<AzureServiceBusQueueResource> WithProperties(
        this IResourceBuilder<AzureServiceBusQueueResource> builder, Action<AzureServiceBusQueueResource> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(builder.Resource);
        return builder;
    }

    /// <summary>Adds a topic (<paramref name="topicName"/> defaults to <paramref name="name"/>).</summary>
    public static IResourceBuilder<AzureServiceBusTopicResource> AddServiceBusTopic(
        this IResourceBuilder<AzureServiceBusResource> builder, string name, string? topicName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var topic = new AzureServiceBusTopicResource(name, topicName ?? name, builder.Resource);
        builder.Resource.Topics.Add(topic);
        return builder.ApplicationBuilder.AddResource(topic).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>Adds a topic named <paramref name="name"/>.</summary>
    public static IResourceBuilder<AzureServiceBusResource> AddTopic(this IResourceBuilder<AzureServiceBusResource> builder, string name)
    {
        builder.AddServiceBusTopic(name);
        return builder;
    }

    /// <summary>Adds a topic named <paramref name="name"/> with the given subscriptions.</summary>
    public static IResourceBuilder<AzureServiceBusResource> AddTopic(
        this IResourceBuilder<AzureServiceBusResource> builder, string name, string[] subscriptions)
    {
        var topic = builder.AddServiceBusTopic(name);
        foreach (var s in subscriptions) topic.AddServiceBusSubscription(s);
        return builder;
    }

    /// <summary>Configures the topic's properties.</summary>
    public static IResourceBuilder<AzureServiceBusTopicResource> WithProperties(
        this IResourceBuilder<AzureServiceBusTopicResource> builder, Action<AzureServiceBusTopicResource> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(builder.Resource);
        return builder;
    }

    /// <summary>Adds a subscription to the topic (<paramref name="subscriptionName"/> defaults to <paramref name="name"/>).</summary>
    public static IResourceBuilder<AzureServiceBusSubscriptionResource> AddServiceBusSubscription(
        this IResourceBuilder<AzureServiceBusTopicResource> builder, string name, string? subscriptionName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var subscription = new AzureServiceBusSubscriptionResource(name, subscriptionName ?? name, builder.Resource);
        builder.Resource.Subscriptions.Add(subscription);
        return builder.ApplicationBuilder.AddResource(subscription).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>Adds a subscription named <paramref name="subscriptionName"/> to the topic named <paramref name="topicName"/>, creating the topic if needed.</summary>
    public static IResourceBuilder<AzureServiceBusResource> AddSubscription(
        this IResourceBuilder<AzureServiceBusResource> builder, string topicName, string subscriptionName)
    {
        var topic = builder.Resource.Topics.FirstOrDefault(t => t.Name == topicName)
            ?? builder.AddServiceBusTopic(topicName).Resource;
        topic.Subscriptions.Add(new AzureServiceBusSubscriptionResource(subscriptionName, subscriptionName, topic));
        return builder;
    }

    /// <summary>Configures the subscription's properties (including <see cref="AzureServiceBusSubscriptionResource.Rules"/>).</summary>
    public static IResourceBuilder<AzureServiceBusSubscriptionResource> WithProperties(
        this IResourceBuilder<AzureServiceBusSubscriptionResource> builder, Action<AzureServiceBusSubscriptionResource> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(builder.Resource);
        return builder;
    }

    /// <summary>
    /// Replaces the default role assignment (Azure Service Bus Data Owner) for the compute environment identity hosting
    /// <paramref name="builder"/> with the given roles on the namespace. Mirrors <c>WithRoleAssignments</c>.
    /// </summary>
    public static IResourceBuilder<T> WithRoleAssignments<T>(
        this IResourceBuilder<T> builder, IResourceBuilder<AzureServiceBusResource> target, params AzureServiceBusRole[] roles)
        where T : IResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(roles);
        foreach (var role in roles)
        {
            if (!Enum.IsDefined(role)) throw new ArgumentException($"'{role}' is not a valid Service Bus role.", nameof(roles));
        }
        return builder.WithAnnotation(new AzureServiceBusRoleAnnotation(target.Resource, roles));
    }
}
