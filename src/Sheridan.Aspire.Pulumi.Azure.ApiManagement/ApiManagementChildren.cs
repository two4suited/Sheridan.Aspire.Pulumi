using Aspire.Hosting.ApplicationModel;

namespace Sheridan.Aspire.Pulumi.Azure.ApiManagement;

/// <summary>A backend service (<c>Microsoft.ApiManagement/service/backends</c>) requests can be forwarded to.</summary>
public sealed class AzureApiManagementBackendResource(string name, string backendName, string url, AzureApiManagementResource parent)
    : Resource(name), IResourceWithParent<AzureApiManagementResource>
{
    public AzureApiManagementResource Parent { get; } = parent;
    /// <summary>The physical backend identifier in API Management.</summary>
    public string BackendName { get; } = backendName;
    public string Url { get; } = url;
    /// <summary>"http" (default) or "soap".</summary>
    public string Protocol { get; set; } = "http";
    /// <summary>Validate the backend's TLS certificate chain and host name (on by default).</summary>
    public bool ValidateCertificateChain { get; set; } = true;
}

/// <summary>An operation (<c>Microsoft.ApiManagement/service/apis/operations</c>) of an API.</summary>
public sealed class AzureApiManagementOperationResource(
    string name, string operationName, string method, string urlTemplate, string displayName, AzureApiManagementApiResource parent)
    : Resource(name), IResourceWithParent<AzureApiManagementApiResource>
{
    public AzureApiManagementApiResource Parent { get; } = parent;
    public string OperationName { get; } = operationName;
    public string Method { get; } = method;
    public string UrlTemplate { get; } = urlTemplate;
    public string DisplayName { get; } = displayName;
    internal string? PolicyXml { get; set; }
    internal List<string> InboundStatements { get; } = [];
}

/// <summary>An API (<c>Microsoft.ApiManagement/service/apis</c>) published through the gateway.</summary>
public sealed class AzureApiManagementApiResource(
    string name, string apiName, string path, string displayName, bool subscriptionRequired, AzureApiManagementResource parent)
    : Resource(name), IResourceWithParent<AzureApiManagementResource>
{
    public AzureApiManagementResource Parent { get; } = parent;
    public string ApiName { get; } = apiName;
    public string Path { get; } = path;
    public string DisplayName { get; } = displayName;
    public bool SubscriptionRequired { get; } = subscriptionRequired;
    public AzureApiManagementBackendResource? Backend { get; internal set; }
    internal string? PolicyXml { get; set; }
    internal List<string> InboundStatements { get; } = [];
    internal List<AzureApiManagementOperationResource> Operations { get; } = [];
    internal AzureApiManagementOpenApiSource? OpenApi { get; set; }
}

/// <summary>The serialization of an inline OpenAPI document.</summary>
public enum AzureApiManagementOpenApiFormat
{
    Json,
    Yaml,
}

/// <summary>Where an API's OpenAPI document comes from; resolved when the Pulumi program is generated.</summary>
internal sealed class AzureApiManagementOpenApiSource
{
    private readonly Func<(string Format, string Value)> _resolve;
    private AzureApiManagementOpenApiSource(Func<(string, string)> resolve) => _resolve = resolve;

    /// <summary>The <c>format</c> and <c>value</c> properties of <c>azure-native:apimanagement:Api</c>.</summary>
    public (string Format, string Value) Resolve() => _resolve();

    public static AzureApiManagementOpenApiSource FromContent(string content, AzureApiManagementOpenApiFormat format) =>
        new(() => (format == AzureApiManagementOpenApiFormat.Json ? "openapi+json" : "openapi", content));

    public static AzureApiManagementOpenApiSource FromUrl(string url) => new(() => ("openapi-link", url));

    public static AzureApiManagementOpenApiSource FromFile(string path, string appHostDirectory)
    {
        var full = System.IO.Path.GetFullPath(path, appHostDirectory);
        return new(() =>
        {
            if (!File.Exists(full))
                throw new FileNotFoundException($"The OpenAPI document '{full}' (from '{path}') does not exist. Build the project that generates it before publishing.", full);
            var content = File.ReadAllText(full);
            if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException($"The OpenAPI document '{full}' is empty.");
            var yaml = System.IO.Path.GetExtension(full).ToLowerInvariant() is ".yaml" or ".yml"
                || (System.IO.Path.GetExtension(full).ToLowerInvariant() != ".json" && !content.TrimStart().StartsWith('{'));
            return (yaml ? "openapi" : "openapi+json", content);
        });
    }
}

/// <summary>A product (<c>Microsoft.ApiManagement/service/products</c>) that groups APIs for consumers.</summary>
public sealed class AzureApiManagementProductResource(
    string name, string productName, string displayName, AzureApiManagementResource parent)
    : Resource(name), IResourceWithParent<AzureApiManagementResource>
{
    public AzureApiManagementResource Parent { get; } = parent;
    public string ProductName { get; } = productName;
    public string DisplayName { get; } = displayName;
    public bool SubscriptionRequired { get; set; } = true;
    public bool ApprovalRequired { get; set; }
    /// <summary>"published" or "notPublished".</summary>
    public string State { get; set; } = "published";
    public int? SubscriptionsLimit { get; set; }
    internal List<AzureApiManagementApiResource> Apis { get; } = [];
}

/// <summary>A named value (<c>Microsoft.ApiManagement/service/namedValues</c>) usable in policies as <c>{{displayName}}</c>.</summary>
public sealed class AzureApiManagementNamedValueResource(
    string name, string namedValueName, string value, bool secret, AzureApiManagementResource parent)
    : Resource(name), IResourceWithParent<AzureApiManagementResource>
{
    public AzureApiManagementResource Parent { get; } = parent;
    public string NamedValueName { get; } = namedValueName;
    public string Value { get; } = value;
    public bool Secret { get; } = secret;
}
