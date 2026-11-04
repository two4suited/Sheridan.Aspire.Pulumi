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
