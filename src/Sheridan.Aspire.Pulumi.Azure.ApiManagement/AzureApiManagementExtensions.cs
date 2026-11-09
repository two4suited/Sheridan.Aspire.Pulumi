using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Provisioning;

namespace Sheridan.Aspire.Pulumi.Azure.ApiManagement;

/// <summary>Pulumi equivalents of the Aspire.Hosting.Azure.ApiManagement extension methods.</summary>
public static class AzureApiManagementExtensions
{
    /// <summary>Adds an Azure API Management service provisioned by the Pulumi stack (the equivalent of AddAzureApiManagement).</summary>
    public static IResourceBuilder<AzureApiManagementResource> AddPulumiAzureApiManagement(
        this IDistributedApplicationBuilder builder, string name, AzureApiManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.PublisherEmail);
        ArgumentException.ThrowIfNullOrEmpty(options.PublisherName);
        if (options.PublisherEmail.Length > 100) throw new ArgumentException("The publisher email address cannot exceed 100 characters.", nameof(options));
        if (options.PublisherName.Length > 100) throw new ArgumentException("The publisher name cannot exceed 100 characters.", nameof(options));
        ValidateCapacity(options.Sku, options.Capacity);
        return builder.AddResource(new AzureApiManagementResource(name, options)).ExcludeFromManifest();
    }

    /// <summary>Alias of <see cref="AddPulumiAzureApiManagement"/>.</summary>
    public static IResourceBuilder<AzureApiManagementResource> AddPulumiApiManagement(
        this IDistributedApplicationBuilder builder, string name, AzureApiManagementOptions options) =>
        builder.AddPulumiAzureApiManagement(name, options);

    /// <summary>Adds an API published under <paramref name="path"/> (the equivalent of <c>AddApi(name, path)</c>).</summary>
    public static IResourceBuilder<AzureApiManagementApiResource> AddApi(
        this IResourceBuilder<AzureApiManagementResource> builder, string name, string path,
        string? displayName = null, bool subscriptionRequired = true, string? apiName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(path);
        var parent = builder.Resource;
        var resolved = apiName ?? name;
        if (parent.Apis.Any(a => string.Equals(a.ApiName, resolved, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"API Management '{parent.Name}' already has an API named '{resolved}'.");
        var api = new AzureApiManagementApiResource(name, resolved, path.Trim('/'), displayName ?? name, subscriptionRequired, parent);
        parent.Apis.Add(api);
        return builder.ApplicationBuilder.AddResource(api).ExcludeFromManifest();
    }

    /// <summary>Adds a backend service API Management can forward requests to (the equivalent of <c>AddBackend</c>).</summary>
    public static IResourceBuilder<AzureApiManagementBackendResource> AddBackend(
        this IResourceBuilder<AzureApiManagementResource> builder, string name, string url,
        string protocol = "http", bool validateCertificateChain = true, string? backendName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException($"'{url}' is not an absolute http(s) URL.", nameof(url));
        if (protocol is not ("http" or "soap")) throw new ArgumentException("The protocol must be 'http' or 'soap'.", nameof(protocol));
        var parent = builder.Resource;
        var resolved = backendName ?? name;
        if (parent.Backends.Any(b => string.Equals(b.BackendName, resolved, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"API Management '{parent.Name}' already has a backend named '{resolved}'.");
        var backend = new AzureApiManagementBackendResource(name, resolved, url, parent) { Protocol = protocol, ValidateCertificateChain = validateCertificateChain };
        parent.Backends.Add(backend);
        return builder.ApplicationBuilder.AddResource(backend).ExcludeFromManifest();
    }

    /// <summary>Routes the API's requests to <paramref name="backend"/> (a <c>set-backend-service</c> inbound policy and, unless operations are modeled or imported from OpenAPI, a wildcard proxy for every method).</summary>
    public static IResourceBuilder<AzureApiManagementApiResource> WithBackend(
        this IResourceBuilder<AzureApiManagementApiResource> builder, IResourceBuilder<AzureApiManagementBackendResource> backend)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(backend);
        if (backend.Resource.Parent != builder.Resource.Parent)
            throw new InvalidOperationException("The backend and the API must belong to the same API Management service.");
        builder.Resource.Backend = backend.Resource;
        return builder;
    }

    /// <summary>
    /// Imports the API's operations from an OpenAPI document on disk. The file is read when the Pulumi program is generated
    /// (so it may be produced by a build step); a relative path is resolved against the AppHost directory. The format (JSON or YAML)
    /// is taken from the extension, or from the content for other extensions.
    /// </summary>
    public static IResourceBuilder<AzureApiManagementApiResource> WithOpenApi(
        this IResourceBuilder<AzureApiManagementApiResource> builder, string path)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        builder.Resource.OpenApi = AzureApiManagementOpenApiSource.FromFile(path, builder.ApplicationBuilder.AppHostDirectory);
        return builder;
    }

    /// <summary>Imports the API's operations from an OpenAPI document that API Management downloads from <paramref name="url"/> (<c>openapi-link</c>).</summary>
    public static IResourceBuilder<AzureApiManagementApiResource> WithOpenApi(
        this IResourceBuilder<AzureApiManagementApiResource> builder, Uri url)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https"))
            throw new ArgumentException($"'{url}' is not an absolute http(s) URL.", nameof(url));
        builder.Resource.OpenApi = AzureApiManagementOpenApiSource.FromUrl(url.AbsoluteUri);
        return builder;
    }

    /// <summary>Imports the API's operations from inline OpenAPI document content.</summary>
    public static IResourceBuilder<AzureApiManagementApiResource> WithOpenApi(
        this IResourceBuilder<AzureApiManagementApiResource> builder, string content, AzureApiManagementOpenApiFormat format)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        builder.Resource.OpenApi = AzureApiManagementOpenApiSource.FromContent(content, format);
        return builder;
    }

    /// <summary>Adds an explicitly modeled operation to the API.</summary>
    public static IResourceBuilder<AzureApiManagementOperationResource> AddOperation(
        this IResourceBuilder<AzureApiManagementApiResource> builder, string name, string method, string urlTemplate,
        string? displayName = null, string? operationName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(method);
        ArgumentException.ThrowIfNullOrEmpty(urlTemplate);
        var api = builder.Resource;
        var resolved = operationName ?? name;
        if (api.Operations.Any(o => string.Equals(o.OperationName, resolved, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"API '{api.Name}' already has an operation named '{resolved}'.");
        var op = new AzureApiManagementOperationResource(name, resolved, method.ToUpperInvariant(), urlTemplate, displayName ?? name, api);
        api.Operations.Add(op);
        return builder.ApplicationBuilder.AddResource(op).ExcludeFromManifest();
    }

    /// <summary>Adds a product grouping APIs for consumers.</summary>
    public static IResourceBuilder<AzureApiManagementProductResource> AddProduct(
        this IResourceBuilder<AzureApiManagementResource> builder, string name, string displayName,
        bool subscriptionRequired = true, bool approvalRequired = false, bool published = true, int? subscriptionsLimit = null, string? productName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(displayName);
        if (displayName.Length > 300) throw new ArgumentException("The product display name cannot exceed 300 characters.", nameof(displayName));
        var parent = builder.Resource;
        var product = new AzureApiManagementProductResource(name, productName ?? name, displayName, parent)
        {
            SubscriptionRequired = subscriptionRequired,
            ApprovalRequired = approvalRequired,
            State = published ? "published" : "notPublished",
            SubscriptionsLimit = subscriptionsLimit,
        };
        parent.Products.Add(product);
        return builder.ApplicationBuilder.AddResource(product).ExcludeFromManifest();
    }

    /// <summary>Associates an API with the product.</summary>
    public static IResourceBuilder<AzureApiManagementProductResource> WithApi(
        this IResourceBuilder<AzureApiManagementProductResource> builder, IResourceBuilder<AzureApiManagementApiResource> api)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(api);
        if (api.Resource.Parent != builder.Resource.Parent)
            throw new InvalidOperationException("The product and the API must belong to the same API Management service.");
        if (!builder.Resource.Apis.Contains(api.Resource)) builder.Resource.Apis.Add(api.Resource);
        return builder;
    }

    /// <summary>Adds a named value usable in policies as <c>{{name}}</c>. Marked secret, it is stored encrypted and hidden.</summary>
    public static IResourceBuilder<AzureApiManagementNamedValueResource> AddNamedValue(
        this IResourceBuilder<AzureApiManagementResource> builder, string name, string value, bool secret = false, string? namedValueName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        var parent = builder.Resource;
        var nv = new AzureApiManagementNamedValueResource(name, namedValueName ?? name, value, secret, parent);
        parent.NamedValues.Add(nv);
        return builder.ApplicationBuilder.AddResource(nv).ExcludeFromManifest();
    }

    /// <summary>Adds inbound policy statements (well-formed XML elements, without a <c>policies</c> envelope) at service scope.</summary>
    public static IResourceBuilder<AzureApiManagementResource> WithInboundPolicy(this IResourceBuilder<AzureApiManagementResource> builder, string policyXml)
    {
        ArgumentNullException.ThrowIfNull(builder);
        AzureApiManagementResource.ValidateFragment(policyXml, nameof(policyXml));
        if (builder.Resource.PolicyXml is not null) throw new InvalidOperationException("A complete policy is already set; it cannot be combined with inbound statements.");
        builder.Resource.InboundStatements.Add(policyXml);
        return builder;
    }

    /// <summary>Sets the complete service-scope policy document (a <c>policies</c> element).</summary>
    public static IResourceBuilder<AzureApiManagementResource> WithPolicy(this IResourceBuilder<AzureApiManagementResource> builder, string policyXml)
    {
        ArgumentNullException.ThrowIfNull(builder);
        AzureApiManagementResource.ValidateFragment(policyXml, nameof(policyXml));
        if (builder.Resource.InboundStatements.Count > 0) throw new InvalidOperationException("Inbound statements are already set; they cannot be combined with a complete policy.");
        builder.Resource.PolicyXml = policyXml;
        return builder;
    }

    /// <summary>Adds inbound policy statements at API scope.</summary>
    public static IResourceBuilder<AzureApiManagementApiResource> WithInboundPolicy(this IResourceBuilder<AzureApiManagementApiResource> builder, string policyXml)
    {
        ArgumentNullException.ThrowIfNull(builder);
        AzureApiManagementResource.ValidateFragment(policyXml, nameof(policyXml));
        if (builder.Resource.PolicyXml is not null) throw new InvalidOperationException("A complete policy is already set; it cannot be combined with inbound statements.");
        builder.Resource.InboundStatements.Add(policyXml);
        return builder;
    }

    /// <summary>Sets the complete API-scope policy document.</summary>
    public static IResourceBuilder<AzureApiManagementApiResource> WithPolicy(this IResourceBuilder<AzureApiManagementApiResource> builder, string policyXml)
    {
        ArgumentNullException.ThrowIfNull(builder);
        AzureApiManagementResource.ValidateFragment(policyXml, nameof(policyXml));
        if (builder.Resource.InboundStatements.Count > 0 || builder.Resource.Backend is not null)
            throw new InvalidOperationException("Inbound statements or a backend are already set; a complete policy replaces them. Include set-backend-service in the policy instead.");
        builder.Resource.PolicyXml = policyXml;
        return builder;
    }

    /// <summary>Adds inbound policy statements at operation scope.</summary>
    public static IResourceBuilder<AzureApiManagementOperationResource> WithInboundPolicy(this IResourceBuilder<AzureApiManagementOperationResource> builder, string policyXml)
    {
        ArgumentNullException.ThrowIfNull(builder);
        AzureApiManagementResource.ValidateFragment(policyXml, nameof(policyXml));
        if (builder.Resource.PolicyXml is not null) throw new InvalidOperationException("A complete policy is already set; it cannot be combined with inbound statements.");
        builder.Resource.InboundStatements.Add(policyXml);
        return builder;
    }

    /// <summary>Pulumi equivalent of <c>ConfigureInfrastructure</c>: customize the API Management service before the program is emitted.</summary>
    public static IResourceBuilder<AzureApiManagementResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureApiManagementResource> builder, Action<PulumiApiManagementInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiApiManagementInfrastructure(infra, owner)));
    }

    private static void ValidateCapacity(AzureApiManagementSku sku, int capacity)
    {
        var (min, max) = sku switch
        {
            AzureApiManagementSku.Consumption => (0, 0),
            AzureApiManagementSku.Developer => (1, 1),
            AzureApiManagementSku.Basic => (1, 2),
            AzureApiManagementSku.BasicV2 => (1, 10),
            AzureApiManagementSku.Standard => (1, 4),
            AzureApiManagementSku.StandardV2 => (1, 10),
            AzureApiManagementSku.Premium => (1, 12),
            AzureApiManagementSku.PremiumV2 => (1, 30),
            _ => throw new ArgumentOutOfRangeException(nameof(sku)),
        };
        if (capacity < min || capacity > max)
            throw new ArgumentException($"The {sku} tier supports a capacity of {(min == max ? min.ToString() : $"{min} to {max}")}, not {capacity}.", "options");
    }
}
