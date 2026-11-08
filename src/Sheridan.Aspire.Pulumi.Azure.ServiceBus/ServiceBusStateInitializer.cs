using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Sheridan.Aspire.Pulumi.Azure.ServiceBus;

/// <summary>
/// Publishes the dashboard state of a Service Bus namespace and its queues, topics and subscriptions in run mode, and makes
/// anything that references them wait until they are Running. With the emulator they follow the emulator container's
/// health (it reports healthy only after loading <c>Config.json</c>); with <c>RunAsExisting</c> they are Running once the
/// endpoint resolves.
/// </summary>
internal static class ServiceBusStateInitializer
{
    internal static void Register(IDistributedApplicationBuilder appBuilder, AzureServiceBusResource sb)
    {
        if (sb.StateInitializerRegistered) return;
        sb.StateInitializerRegistered = true;

        if (sb.Emulator is { } emulator)
        {
            appBuilder.Eventing.Subscribe<ResourceReadyEvent>(emulator, async (@event, _) =>
            {
                var notifications = @event.Services.GetRequiredService<ResourceNotificationService>();
                await PublishAsync(notifications, Related(sb), KnownResourceStates.Running).ConfigureAwait(false);
            });

            // A crashed or failed emulator must fail explicit WaitFor(queue/topic/subscription) instead of leaving it hanging.
            appBuilder.Eventing.Subscribe<AfterResourcesCreatedEvent>((@event, ct) =>
            {
                var notifications = @event.Services.GetRequiredService<ResourceNotificationService>();
                var stopping = @event.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
                _ = Task.Run(() => WatchEmulatorAsync(notifications, emulator, sb, stopping), CancellationToken.None);
                return Task.CompletedTask;
            });
        }
        else
        {
            appBuilder.Eventing.Subscribe<AfterResourcesCreatedEvent>(async (@event, ct) =>
            {
                var notifications = @event.Services.GetRequiredService<ResourceNotificationService>();
                string state;
                try
                {
                    // Throws when a parameter supplying the endpoint has no value.
                    await sb.EndpointExpression.GetValueAsync(ct).ConfigureAwait(false);
                    state = KnownResourceStates.Running;
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    state = KnownResourceStates.FailedToStart;
                }
                await PublishAsync(notifications, Related(sb), state).ConfigureAwait(false);
            });
        }

        // Anything that references the namespace or one of its entities waits for that entity to be Running.
        appBuilder.Eventing.Subscribe<BeforeStartEvent>((@event, _) =>
        {
            AddWaits(@event.Model.Resources, sb);
            return Task.CompletedTask;
        });
    }

    internal static void AddWaits(IEnumerable<IResource> resources, AzureServiceBusResource sb)
    {
        var related = Related(sb).ToHashSet();
        foreach (var resource in resources)
        {
            if (related.Contains(resource) || resource == sb.Emulator || resource == sb.Emulator?.SqlServer) continue;
            foreach (var target in resource.Annotations.OfType<ResourceRelationshipAnnotation>()
                .Where(a => a.Type == "Reference" && related.Contains(a.Resource)).Select(a => a.Resource).ToList())
            {
                if (!resource.Annotations.OfType<WaitAnnotation>().Any(w => w.Resource == target))
                    resource.Annotations.Add(new WaitAnnotation(target, WaitType.WaitUntilHealthy));
            }
        }
    }

    /// <summary>The namespace and every declared queue, topic and subscription.</summary>
    internal static IEnumerable<IResource> Related(AzureServiceBusResource sb) =>
        [sb, .. sb.Queues, .. sb.Topics, .. sb.Topics.SelectMany(t => t.Subscriptions)];

    private static async Task PublishAsync(ResourceNotificationService notifications, IEnumerable<IResource> resources, string state)
    {
        foreach (var r in resources)
            await notifications.PublishUpdateAsync(r, s => s with { State = new ResourceStateSnapshot(state, state == KnownResourceStates.Running ? KnownResourceStateStyles.Success : KnownResourceStateStyles.Error) }).ConfigureAwait(false);
    }

    private static async Task WatchEmulatorAsync(ResourceNotificationService notifications, AzureServiceBusEmulatorResource emulator, AzureServiceBusResource sb, CancellationToken ct)
    {
        try
        {
            await foreach (var e in notifications.WatchAsync(ct).ConfigureAwait(false))
            {
                if (e.Resource != emulator) continue;
                var state = e.Snapshot.State?.Text;
                if (state is "FailedToStart" or "Exited" or "RuntimeUnhealthy")
                    await PublishAsync(notifications, Related(sb), KnownResourceStates.FailedToStart).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }
}
