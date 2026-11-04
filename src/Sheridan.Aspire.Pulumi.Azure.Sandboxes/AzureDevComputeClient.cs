using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace Sheridan.Aspire.Pulumi.Azure.Sandboxes;

// The Azure Dev Compute data plane (https://management.{region}.azuredevcompute.io) that creates the disk images, sandboxes and
// ports of a sandbox group. It is not an ARM API, so no Pulumi provider (azure-native, azapi) models it; the Aspire pipeline
// calls it directly, exactly as the official Aspire.Hosting.Azure.Sandboxes does.

internal sealed record AzureDevComputeResourceScope(string SubscriptionId, string ResourceGroupName, string SandboxGroupName, string Region);

internal sealed class AzureDevComputeDiskImageStatus { public string State { get; init; } = ""; public string? ErrorMessage { get; init; } }
internal sealed class AzureDevComputeDiskImage
{
    public string Id { get; init; } = "";
    public Dictionary<string, string> Labels { get; init; } = [];
    public AzureDevComputeDiskImageStatus? Status { get; init; }
}
internal sealed class AzureDevComputeDiskImageSource
{
    public string Kind { get; init; } = "registry";
    public required string ImageUrl { get; init; }
    public string? ManagedIdentityClientId { get; init; }
}
internal sealed class AzureDevComputeCreateDiskImageRequest
{
    public string? Name { get; init; }
    public Dictionary<string, string> Labels { get; init; } = [];
    public required AzureDevComputeDiskImageSource Source { get; init; }
}
internal sealed class AzureDevComputeSandboxPort { public string? Name { get; init; } public int Port { get; init; } public Uri? Url { get; init; } }
internal sealed class AzureDevComputeSandbox
{
    public string Id { get; init; } = "";
    public Dictionary<string, string> Labels { get; init; } = [];
    public List<AzureDevComputeSandboxPort> Ports { get; init; } = [];
}
internal sealed class AzureDevComputePortsList { public List<AzureDevComputeSandboxPort> Ports { get; init; } = []; }
internal sealed class AzureDevComputeIdentitySetting { public required string Identity { get; init; } public string Lifecycle { get; init; } = "All"; }
internal sealed class AzureDevComputeSandboxEgressHostRule { public string Action { get; init; } = "Allow"; public required string Pattern { get; init; } }
internal sealed class AzureDevComputeSandboxEgressPolicy
{
    public string DefaultAction { get; init; } = "Deny";
    public string? TrafficInspection { get; init; }
    public List<AzureDevComputeSandboxEgressHostRule> HostRules { get; init; } = [];
}
internal sealed class AzureDevComputeSandboxResources { public string Cpu { get; init; } = "1000m"; public string Memory { get; init; } = "2048Mi"; public string Disk { get; init; } = "20480Mi"; }
internal sealed class AzureDevComputeSandboxDiskImageSource { public required string Id { get; init; } public bool IsPublic { get; init; } }
internal sealed class AzureDevComputeSandboxSource { public required AzureDevComputeSandboxDiskImageSource DiskImage { get; init; } }
internal sealed class AzureDevComputeSandboxRequest
{
    public Dictionary<string, string> Labels { get; init; } = [];
    public List<string>? Entrypoint { get; init; }
    public List<string>? Cmd { get; init; }
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
    public List<AzureDevComputeIdentitySetting>? IdentitySettings { get; init; }
    public bool? SkipEgressProxy { get; init; }
    public AzureDevComputeSandboxEgressPolicy? EgressPolicy { get; init; }
    public required AzureDevComputeSandboxSource SourcesRef { get; init; }
    public required AzureDevComputeSandboxResources Resources { get; init; }
}
internal sealed class AzureDevComputeSandboxAutoSuspendPolicy { public bool Enabled { get; init; } public int? Interval { get; init; } public string? Mode { get; init; } }
internal sealed class AzureDevComputeSandboxAutoDeletePolicy { public bool Enabled { get; init; } public long? DeleteIntervalInSeconds { get; init; } public string? Trigger { get; init; } }
internal sealed class AzureDevComputeSandboxLifecyclePolicy
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public AzureDevComputeSandboxAutoSuspendPolicy? AutoSuspendPolicy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public AzureDevComputeSandboxAutoDeletePolicy? AutoDeletePolicy { get; init; }
}
internal sealed class AzureDevComputePortEntraIdAuthConfig { public bool Enabled { get; init; } public IReadOnlyList<string> ObjectIds { get; init; } = []; public IReadOnlyList<string> TenantIds { get; init; } = []; }
internal sealed class AzureDevComputePortAuthConfig { public bool? Anonymous { get; init; } public AzureDevComputePortEntraIdAuthConfig? EntraId { get; init; } }
internal sealed class AzureDevComputeAddPortRequest
{
    public string? Name { get; init; }
    public required int Port { get; init; }
    public string? ActivationMode { get; init; }
    public AzureDevComputePortAuthConfig? Auth { get; init; }
    public required string Protocol { get; init; }
}
internal sealed class AzureDevComputeRemovePortRequest { public required int Port { get; init; } }

/// <summary>Minimal client for the Azure Dev Compute sandbox data plane (api-version 2026-02-01-preview), as used by the official integration.</summary>
internal sealed class AzureDevComputeClient(HttpClient httpClient, TokenCredential credential, ILogger logger, TimeSpan? retryDelay = null)
{
    internal const string AuthorizationScope = "https://management.azuredevcompute.io/.default";
    private const string ApiVersion = "2026-02-01-preview";
    private const int MaxRetryCount = 6;
    private const int MaxForbiddenRetryCount = 20;
    private const int PageSize = 100;

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private AccessToken _accessToken;

    public Task<AzureDevComputeDiskImage> CreateDiskImageAsync(AzureDevComputeResourceScope scope, AzureDevComputeCreateDiskImageRequest request, CancellationToken ct) =>
        SendAsync<AzureDevComputeDiskImage>(scope, HttpMethod.Put, GroupPath(scope) + "/diskimages/v2", request, ct);

    public Task<List<AzureDevComputeDiskImage>> ListDiskImagesAsync(AzureDevComputeResourceScope scope, string? labels, CancellationToken ct) =>
        ListAllPagesAsync<AzureDevComputeDiskImage>(scope, "diskimages", labels, ct);

    public Task<AzureDevComputeDiskImage> GetDiskImageAsync(AzureDevComputeResourceScope scope, string id, CancellationToken ct) =>
        SendAsync<AzureDevComputeDiskImage>(scope, HttpMethod.Get, GroupPath(scope) + "/diskimages/" + Escape(id), null, ct);

    public Task DeleteDiskImageAsync(AzureDevComputeResourceScope scope, string id, CancellationToken ct) =>
        SendAsync(scope, HttpMethod.Delete, GroupPath(scope) + "/diskimages/" + Escape(id), null, ct, allowNotFound: true);

    public Task<List<AzureDevComputeSandbox>> ListSandboxesAsync(AzureDevComputeResourceScope scope, string? labels, CancellationToken ct) =>
        ListAllPagesAsync<AzureDevComputeSandbox>(scope, "sandboxes", labels, ct);

    public Task<AzureDevComputeSandbox> CreateSandboxAsync(AzureDevComputeResourceScope scope, AzureDevComputeSandboxRequest request, CancellationToken ct) =>
        SendAsync<AzureDevComputeSandbox>(scope, HttpMethod.Put, GroupPath(scope) + "/sandboxes", request, ct);

    public Task<AzureDevComputeSandbox> SetLifecycleAsync(AzureDevComputeResourceScope scope, string sandboxId, AzureDevComputeSandboxLifecyclePolicy lifecycle, CancellationToken ct) =>
        SendAsync<AzureDevComputeSandbox>(scope, HttpMethod.Post, GroupPath(scope) + "/sandboxes/" + Escape(sandboxId) + "/lifecycle", lifecycle, ct);

    public async Task<List<AzureDevComputeSandboxPort>> AddPortAsync(AzureDevComputeResourceScope scope, string sandboxId, AzureDevComputeAddPortRequest request, CancellationToken ct) =>
        (await SendAsync<AzureDevComputePortsList>(scope, HttpMethod.Post, GroupPath(scope) + "/sandboxes/" + Escape(sandboxId) + "/ports/add", request, ct)).Ports;

    public Task RemovePortAsync(AzureDevComputeResourceScope scope, string sandboxId, AzureDevComputeRemovePortRequest request, CancellationToken ct) =>
        SendAsync(scope, HttpMethod.Post, GroupPath(scope) + "/sandboxes/" + Escape(sandboxId) + "/ports/remove", request, ct, allowNotFound: true);

    public Task DeleteSandboxAsync(AzureDevComputeResourceScope scope, string sandboxId, CancellationToken ct) =>
        SendAsync(scope, HttpMethod.Delete, GroupPath(scope) + "/sandboxes/" + Escape(sandboxId), null, ct, allowNotFound: true);

    private async Task<List<T>> ListAllPagesAsync<T>(AzureDevComputeResourceScope scope, string resourceType, string? labels, CancellationToken ct)
    {
        var results = new List<T>();
        for (var page = 1; ; page++)
        {
            var path = $"{GroupPath(scope)}/{resourceType}?Page={page}&PageSize={PageSize}";
            if (!string.IsNullOrWhiteSpace(labels)) path += "&labels=" + WebUtility.UrlEncode(labels);
            var list = await SendAsync<List<T>>(scope, HttpMethod.Get, path, null, ct);
            results.AddRange(list);
            if (list.Count < PageSize) return results;
        }
    }

    private async Task SendAsync(AzureDevComputeResourceScope scope, HttpMethod method, string path, object? content, CancellationToken ct, bool allowNotFound = false)
    {
        using var response = await SendWithRetryAsync(scope, method, path, content, ct);
        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return;
        EnsureSuccess(response, method, path);
    }

    private async Task<T> SendAsync<T>(AzureDevComputeResourceScope scope, HttpMethod method, string path, object? content, CancellationToken ct)
    {
        using var response = await SendWithRetryAsync(scope, method, path, content, ct);
        EnsureSuccess(response, method, path);
        return await response.Content.ReadFromJsonAsync<T>(s_json, ct)
            ?? throw new InvalidOperationException($"Sandbox data plane request '{method} {path}' returned an empty response.");
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(AzureDevComputeResourceScope scope, HttpMethod method, string path, object? content, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await SendCoreAsync(scope, method, path, content, ct);
            }
            catch (HttpRequestException e) when (attempt < MaxRetryCount && IsIdempotent(method))
            {
                logger.LogInformation(e, "Sandbox data plane request {Method} {Path} failed with a transient network error; retrying.", method.Method, path);
                await Task.Delay(retryDelay ?? TimeSpan.FromSeconds(5), ct);
                continue;
            }

            if (response.StatusCode == HttpStatusCode.Forbidden && attempt < MaxForbiddenRetryCount)
            {
                // The Container Apps SandboxGroup Data Owner role assignment can take a while to propagate after the stack deploys.
                _accessToken = default;
                response.Dispose();
                logger.LogWarning("Sandbox data plane request {Method} {Path} returned HTTP 403; waiting for the SandboxGroup Data Owner role assignment to propagate (retry {Attempt} of {Max}).",
                    method.Method, path, attempt + 1, MaxForbiddenRetryCount);
                await Task.Delay(retryDelay ?? TimeSpan.FromSeconds(30), ct);
                continue;
            }

            var transient = response.StatusCode == HttpStatusCode.TooManyRequests
                || (response.StatusCode >= HttpStatusCode.InternalServerError && IsIdempotent(method));
            if (!transient || attempt >= MaxRetryCount) return response;

            var delay = response.Headers.RetryAfter?.Delta ?? retryDelay ?? TimeSpan.FromSeconds(5);
            response.Dispose();
            await Task.Delay(delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay, ct);
        }
    }

    private static bool IsIdempotent(HttpMethod m) => m == HttpMethod.Get || m == HttpMethod.Delete;

    private async Task<HttpResponseMessage> SendCoreAsync(AzureDevComputeResourceScope scope, HttpMethod method, string path, object? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, CreateRequestUri(scope, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(ct));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (content is not null) request.Content = JsonContent.Create(content, content.GetType(), null, s_json);
        logger.LogInformation("Sending sandbox data plane request: {Method} {Path}", method.Method, request.RequestUri!.PathAndQuery);
        return await httpClient.SendAsync(request, ct);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken.Token is null || _accessToken.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
            _accessToken = await credential.GetTokenAsync(new TokenRequestContext([AuthorizationScope]), ct);
        return _accessToken.Token;
    }

    private static void EnsureSuccess(HttpResponseMessage response, HttpMethod method, string path)
    {
        if (response.IsSuccessStatusCode) return;
        var forbidden = response.StatusCode == HttpStatusCode.Forbidden
            ? " Verify that the calling principal has the Container Apps SandboxGroup Data Owner role on the sandbox group; newly-created role assignments can take a short time to propagate."
            : "";
        throw new InvalidOperationException($"Sandbox data plane request '{method} {path}' failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{forbidden}");
    }

    private static string GroupPath(AzureDevComputeResourceScope s) =>
        $"subscriptions/{Escape(s.SubscriptionId)}/resourceGroups/{Escape(s.ResourceGroupName)}/sandboxGroups/{Escape(s.SandboxGroupName)}";

    private static Uri CreateRequestUri(AzureDevComputeResourceScope scope, string path)
    {
        var q = path.IndexOf('?');
        var query = q >= 0 ? path[(q + 1)..] + "&" : "";
        return new UriBuilder(Uri.UriSchemeHttps, $"management.{scope.Region}.azuredevcompute.io")
        {
            Path = q >= 0 ? path[..q] : path,
            Query = query + "api-version=" + ApiVersion,
        }.Uri;
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
