using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace Sheridan.Aspire.Pulumi.Azure.ServiceBus;

/// <summary>
/// Gives the namespace (and its queues, topics and subscriptions) a lifecycle while it runs as the emulator, so
/// <c>WaitFor(serviceBus)</c> behaves as in the official integration: the namespace is Running (and so Healthy) once the
/// emulator, and the SQL Server behind it, are ready.
/// </summary>
internal static class AzureServiceBusEmulatorLifecycle
{
    internal static IEnumerable<IResource> Entities(AzureServiceBusResource sb) =>
        [.. sb.Queues, .. sb.Topics, .. sb.Topics.SelectMany(t => t.Subscriptions)];

    internal static void Register(IDistributedApplicationBuilder appBuilder, AzureServiceBusResource sb, AzureServiceBusEmulatorResource emulator)
    {
        // The emulator's HTTP health check passes only once it has connected to SQL Server and loaded its configuration.
        appBuilder.Eventing.Subscribe<ResourceReadyEvent>(emulator, async (@event, _) =>
        {
            var notifications = @event.Services.GetRequiredService<ResourceNotificationService>();
            await notifications.PublishUpdateAsync(sb, s => s with { State = KnownResourceStates.Running }).ConfigureAwait(false);
            foreach (var entity in Entities(sb))
                await notifications.PublishUpdateAsync(entity, s => s with { State = KnownResourceStates.Running }).ConfigureAwait(false);
        });

        // Anything that references the namespace (or one of its entities) waits for the emulator without an explicit WaitFor.
        appBuilder.Eventing.Subscribe<BeforeStartEvent>((@event, _) =>
        {
            AddWaits(@event.Model.Resources, sb, emulator);
            return Task.CompletedTask;
        });
    }

    internal static void AddWaits(IEnumerable<IResource> resources, AzureServiceBusResource sb, AzureServiceBusEmulatorResource emulator)
    {
        var related = new HashSet<IResource>([sb, .. Entities(sb)]);
        foreach (var resource in resources)
        {
            if (related.Contains(resource) || resource == emulator) continue;
            var references = resource.Annotations.OfType<ResourceRelationshipAnnotation>()
                .Any(a => a.Type == "Reference" && related.Contains(a.Resource));
            // Entities are only Running together with the namespace, so everything waits on the namespace.
            if (references && !resource.Annotations.OfType<WaitAnnotation>().Any(w => w.Resource == sb))
                resource.Annotations.Add(new WaitAnnotation(sb, WaitType.WaitUntilHealthy));
        }
    }
}
