using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sheridan.Aspire.Pulumi.Azure.CosmosDB;

/// <summary>
/// Creates the declared databases and containers in the local Cosmos DB emulator once it is ready, using the emulator's
/// REST data plane with the well-known master key (no Cosmos SDK dependency, so it works for the vNext emulator on arm64).
/// </summary>
internal static class CosmosDBEmulatorInitializer
{
    private const string ApiVersion = "2018-12-31";

    /// <summary>Wires the emulator's ready event to database/container creation and makes referencing resources wait for it.</summary>
    internal static void Register(IDistributedApplicationBuilder appBuilder, AzureCosmosDBResource cosmos, AzureCosmosDBEmulatorResource emulator)
    {
        appBuilder.Eventing.Subscribe<ResourceReadyEvent>(emulator, async (@event, ct) =>
        {
            var notifications = @event.Services.GetRequiredService<ResourceNotificationService>();
            var logger = @event.Services.GetRequiredService<ResourceLoggerService>().GetLogger(cosmos);
            try
            {
                var endpoint = new Uri(emulator.GetEndpoint("emulator").Url);
                await CreateAsync(endpoint, cosmos, logger, ct).ConfigureAwait(false);
                await notifications.PublishUpdateAsync(cosmos, s => s with { State = KnownResourceStates.Running }).ConfigureAwait(false);
                foreach (var db in cosmos.Databases)
                {
                    await notifications.PublishUpdateAsync(db, s => s with { State = KnownResourceStates.Running }).ConfigureAwait(false);
                    foreach (var c in db.Containers)
                        await notifications.PublishUpdateAsync(c, s => s with { State = KnownResourceStates.Running }).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to create databases and containers in the Cosmos DB emulator.");
                await notifications.PublishUpdateAsync(cosmos, s => s with { State = KnownResourceStates.FailedToStart }).ConfigureAwait(false);
            }
        });

        // Anything that references the account (or one of its databases/containers) waits until they exist.
        appBuilder.Eventing.Subscribe<BeforeStartEvent>((@event, _) =>
        {
            var related = new HashSet<IResource>([cosmos, .. cosmos.Databases, .. cosmos.Databases.SelectMany(d => d.Containers)]);
            foreach (var resource in @event.Model.Resources)
            {
                if (related.Contains(resource) || resource == emulator) continue;
                var references = resource.Annotations.OfType<ResourceRelationshipAnnotation>()
                    .Where(a => a.Type == "Reference" && related.Contains(a.Resource))
                    .Select(a => a.Resource).ToList();
                foreach (var target in references)
                {
                    // Databases and containers are only Running once the declared set has been created; wait on the account.
                    var waitTarget = target is AzureCosmosDBResource ? target : cosmos;
                    if (!resource.Annotations.OfType<WaitAnnotation>().Any(w => w.Resource == waitTarget))
                        resource.Annotations.Add(new WaitAnnotation(waitTarget, WaitType.WaitUntilHealthy));
                }
            }
            return Task.CompletedTask;
        });
    }

    internal static async Task CreateAsync(Uri endpoint, AzureCosmosDBResource cosmos, ILogger logger, CancellationToken ct)
    {
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
        using var http = new HttpClient(handler) { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(30) };

        foreach (var db in cosmos.Databases)
        {
            await SendWithRetryAsync(http, "dbs", "", BuildDatabaseBody(db), logger, $"database '{db.DatabaseName}'", ct).ConfigureAwait(false);
            foreach (var container in db.Containers)
            {
                await SendWithRetryAsync(http, "colls", $"dbs/{db.DatabaseName}", BuildContainerBody(container), logger,
                    $"container '{db.DatabaseName}/{container.ContainerName}'", ct).ConfigureAwait(false);
            }
        }
    }

    internal static string BuildDatabaseBody(AzureCosmosDBDatabaseResource db) =>
        JsonSerializer.Serialize(new { id = db.DatabaseName });

    internal static string BuildContainerBody(AzureCosmosDBContainerResource container)
    {
        var multi = container.PartitionKeyPaths.Count > 1;
        return JsonSerializer.Serialize(new
        {
            id = container.ContainerName,
            partitionKey = new
            {
                paths = container.PartitionKeyPaths,
                kind = multi ? "MultiHash" : "Hash",
                version = 2,
            },
        });
    }

    /// <summary>The Cosmos DB master-key authorization header value for a request.</summary>
    internal static string Authorization(string verb, string resourceType, string resourceLink, string date, string key)
    {
        var payload = $"{verb.ToLowerInvariant()}\n{resourceType.ToLowerInvariant()}\n{resourceLink}\n{date.ToLowerInvariant()}\n\n";
        using var hmac = new HMACSHA256(Convert.FromBase64String(key));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        return Uri.EscapeDataString($"type=master&ver=1.0&sig={signature}");
    }

    private static async Task SendWithRetryAsync(HttpClient http, string resourceType, string resourceLink, string body, ILogger logger, string description, CancellationToken ct)
    {
        // The emulator can report ready slightly before the data plane accepts writes, so retry transient failures.
        const int maxAttempts = 30;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var date = DateTime.UtcNow.ToString("r");
                using var request = new HttpRequestMessage(HttpMethod.Post, resourceLink.Length == 0 ? resourceType : $"{resourceLink}/{resourceType}")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
                request.Headers.TryAddWithoutValidation("x-ms-date", date);
                request.Headers.TryAddWithoutValidation("x-ms-version", ApiVersion);
                request.Headers.TryAddWithoutValidation("Authorization", Authorization("post", resourceType, resourceLink, date, AzureCosmosDBEmulatorResource.WellKnownKey));
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Conflict)
                {
                    logger.LogInformation("Cosmos DB emulator: ensured {Description}.", description);
                    return;
                }
                var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (attempt >= maxAttempts || (int)response.StatusCode is >= 400 and < 500 and not 408 and not 429)
                    throw new InvalidOperationException($"Creating {description} failed with {(int)response.StatusCode}: {text}");
            }
            catch (HttpRequestException) when (attempt < maxAttempts) { }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested && attempt < maxAttempts) { }

            await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
        }
    }
}
