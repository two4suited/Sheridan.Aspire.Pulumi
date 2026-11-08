using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sheridan.Aspire.Pulumi.Azure.Storage;

/// <summary>SDK clients for the storage connection values (an Azurite connection string, or a service endpoint reached with the developer's Azure credentials).</summary>
internal static class StorageClients
{
    /// <summary>Resolves a connection expression. Used from health check factories, which are synchronous; the value is already available by then.</summary>
    internal static string Resolve(ReferenceExpression expression) =>
        expression.GetValueAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult()
        ?? throw new InvalidOperationException("The storage connection string is not available.");

    internal static BlobServiceClient Blob(string connection) =>
        Uri.TryCreate(connection, UriKind.Absolute, out var uri) ? new BlobServiceClient(uri, new DefaultAzureCredential()) : new BlobServiceClient(connection);

    internal static QueueServiceClient Queue(string connection) =>
        Uri.TryCreate(connection, UriKind.Absolute, out var uri) ? new QueueServiceClient(uri, new DefaultAzureCredential()) : new QueueServiceClient(connection);
}

/// <summary>
/// Creates the declared blob containers and queues in the local Azurite emulator once it is ready (like the official
/// <c>RunAsEmulator</c>), publishes the state of the account and its children, and makes referencing resources wait for it.
/// </summary>
internal static class AzureStorageEmulatorInitializer
{
    /// <summary>Wires the emulator's health check, its ready event (container and queue creation) and the wait for referencing resources.</summary>
    internal static void Register(
        IDistributedApplicationBuilder appBuilder, IResourceBuilder<AzureStorageResource> storageBuilder,
        AzureStorageEmulatorResource emulator, IResourceBuilder<AzureStorageEmulatorResource> emulatorBuilder)
    {
        var storage = storageBuilder.Resource;

        // The emulator is ready (and its ResourceReadyEvent fires) once the blob service answers.
        var emulatorCheck = $"{emulator.Name}_check";
        BlobServiceClient? probe = null;
        appBuilder.Services.AddHealthChecks().AddAzureBlobStorage(
            _ => probe ??= StorageClients.Blob(StorageClients.Resolve(storage.BlobConnection)), name: emulatorCheck);
        emulatorBuilder.WithHealthCheck(emulatorCheck);

        // The account is Running once the declared containers and queues exist; its own check makes WaitFor(storage) meaningful.
        var accountCheck = $"{storage.Name}_check";
        BlobServiceClient? client = null;
        appBuilder.Services.AddHealthChecks().AddAzureBlobStorage(
            _ => client ??= StorageClients.Blob(StorageClients.Resolve(storage.BlobConnection)), name: accountCheck);
        storageBuilder.WithHealthCheck(accountCheck);

        appBuilder.Eventing.Subscribe<ResourceReadyEvent>(emulator, async (@event, ct) =>
        {
            var notifications = @event.Services.GetRequiredService<ResourceNotificationService>();
            var logger = @event.Services.GetRequiredService<ResourceLoggerService>().GetLogger(storage);
            try
            {
                var blobs = StorageClients.Blob((await storage.BlobConnection.GetValueAsync(ct).ConfigureAwait(false))!);
                var queues = StorageClients.Queue((await storage.QueueConnection.GetValueAsync(ct).ConfigureAwait(false))!);
                await CreateAsync(blobs, queues, storage, ct).ConfigureAwait(false);
                await storage.PublishStateAsync(notifications, KnownResourceStates.Running).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to create the blob containers and queues in the Azure Storage emulator.");
                // Explicit WaitFor(container/queue) must fail too instead of hanging.
                await storage.PublishStateAsync(notifications, KnownResourceStates.FailedToStart).ConfigureAwait(false);
            }
        });

        // Anything that references the account (or one of its children) waits until the declared containers and queues exist.
        appBuilder.Eventing.Subscribe<BeforeStartEvent>((@event, _) =>
        {
            storage.AddWaitForReferencing(@event.Model, emulator);
            return Task.CompletedTask;
        });
    }

    /// <summary>Creates every declared blob container and queue (existing ones are left as they are).</summary>
    internal static async Task CreateAsync(BlobServiceClient blobs, QueueServiceClient queues, AzureStorageResource storage, CancellationToken ct)
    {
        foreach (var container in storage.BlobContainers)
            await blobs.GetBlobContainerClient(container.BlobContainerName).CreateIfNotExistsAsync(cancellationToken: ct).ConfigureAwait(false);

        foreach (var queue in storage.Queues)
            await queues.GetQueueClient(queue.QueueName).CreateIfNotExistsAsync(cancellationToken: ct).ConfigureAwait(false);
    }
}

/// <summary>Run-mode wiring for an account that is not emulated: <c>RunAsExisting</c> points the app at a real account, so there is nothing to create.</summary>
internal static class AzureStorageExistingInitializer
{
    /// <summary>Publishes Running for the account and its children once the supplied endpoint resolves, and adds the account health check.</summary>
    internal static void Register(IDistributedApplicationBuilder appBuilder, IResourceBuilder<AzureStorageResource> storageBuilder)
    {
        var storage = storageBuilder.Resource;

        var accountCheck = $"{storage.Name}_check";
        BlobServiceClient? client = null;
        appBuilder.Services.AddHealthChecks().AddAzureBlobStorage(
            _ => client ??= StorageClients.Blob(StorageClients.Resolve(storage.BlobConnection)), name: accountCheck);
        storageBuilder.WithHealthCheck(accountCheck);

        appBuilder.Eventing.Subscribe<InitializeResourceEvent>(storage, async (@event, ct) =>
        {
            try
            {
                await storage.BlobConnection.GetValueAsync(ct).ConfigureAwait(false);
                await storage.PublishStateAsync(@event.Notifications, KnownResourceStates.Running).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                @event.Logger.LogError(ex, "The storage endpoint for '{Resource}' could not be resolved.", storage.Name);
                await storage.PublishStateAsync(@event.Notifications, KnownResourceStates.FailedToStart).ConfigureAwait(false);
            }
        });
    }
}
