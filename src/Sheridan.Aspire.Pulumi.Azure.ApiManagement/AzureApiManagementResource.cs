using System.Xml.Linq;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Sheridan.Aspire.Pulumi.Azure.Base;

namespace Sheridan.Aspire.Pulumi.Azure.ApiManagement;

/// <summary>
/// An Azure API Management service provisioned by Pulumi (azure-native), mirroring Aspire.Hosting.Azure.ApiManagement:
/// <c>Microsoft.ApiManagement/service</c> with a system-assigned identity, plus the APIs, operations, backends, policies,
/// products and named values declared beneath it.
/// </summary>
public sealed class AzureApiManagementResource(string name, AzureApiManagementOptions? options = null) : PulumiAzureResource(name)
{
    /// <summary>Options for a new service; null for a service that is only referenced via <c>AsExisting</c>.</summary>
    public AzureApiManagementOptions? Options { get; } = options;

    /// <summary>Name of an existing API Management service (set by <c>AsExisting</c>/<c>PublishAsExisting</c>); when set, the service itself is only looked up, never created or modified.</summary>
    public string? ExistingServiceName { get; internal set; }

    /// <summary>Resource group of the existing service (set by <c>AsExisting</c>); null means the resource group configured for this resource.</summary>
    public string? ExistingServiceResourceGroup { get; internal set; }

    internal List<AzureApiManagementApiResource> Apis { get; } = [];
    internal List<AzureApiManagementBackendResource> Backends { get; } = [];
    internal List<AzureApiManagementProductResource> Products { get; } = [];
    internal List<AzureApiManagementNamedValueResource> NamedValues { get; } = [];
    internal List<string> InboundStatements { get; } = [];
    internal string? PolicyXml { get; set; }

    internal PulumiOutputReference OutputRef(string suffix) =>
        Output(Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack()."), suffix);

    /// <summary>The gateway URL, e.g. <c>https://apim-xyz.azure-api.net</c>.</summary>
    public PulumiOutputReference GatewayUrl => OutputRef("GatewayUrl");
    public PulumiOutputReference NameOutput => OutputRef("Name");
    /// <summary>Principal id of the service's system-assigned identity (for role assignments on backend targets).</summary>
    /// <remarks>Not available for an existing service (its identity is not managed here).</remarks>
    public PulumiOutputReference PrincipalId => ExistingServiceName is null
        ? OutputRef("PrincipalId")
        : throw new InvalidOperationException($"'{Name}' is an existing API Management service; its principal id is not exposed.");

    private static string Child(string parentId, string name)
    {
        var id = PulumiAzureProgramExtensions.ToPulumiId(name);
        return parentId + char.ToUpperInvariant(id[0]) + id[1..];
    }

    /// <summary>Wraps inbound statements (or a complete policy document) in a <c>policies</c> envelope.</summary>
    internal static string? BuildPolicy(string? complete, IReadOnlyList<string> inbound)
    {
        if (complete is not null) return complete;
        if (inbound.Count == 0) return null;
        return "<policies><inbound><base />" + string.Concat(inbound) + "</inbound><backend><base /></backend><outbound><base /></outbound><on-error><base /></on-error></policies>";
    }

    /// <summary>Throws an <see cref="ArgumentException"/> unless <paramref name="xml"/> is one or more well-formed XML elements.</summary>
    internal static void ValidateFragment(string xml, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml, paramName);
        try { XElement.Parse($"<r>{xml}</r>"); }
        catch (System.Xml.XmlException e) { throw new ArgumentException($"The policy is not well-formed XML: {e.Message}", paramName); }
    }

    /// <summary>The <c>azure-native:apimanagement:Api</c> properties; an OpenAPI document adds <c>format</c>/<c>value</c> and supplies the operations.</summary>
    internal static Dictionary<string, object?> ApiProperties(AzureApiManagementApiResource api)
    {
        var props = new Dictionary<string, object?>
        {
            ["apiId"] = api.ApiName,
            ["path"] = api.Path,
            ["displayName"] = api.DisplayName,
            ["protocols"] = new List<object?> { "https" },
            ["subscriptionRequired"] = api.SubscriptionRequired,
        };
        if (api.OpenApi is { } openApi)
        {
            var (format, value) = openApi.Resolve();
            props["format"] = format;
            props["value"] = value;
        }
        return props;
    }

    /// <summary>Modeled operations; a routed API with neither modeled operations nor an OpenAPI document gets a wildcard proxy for every method.</summary>
    internal static IReadOnlyList<AzureApiManagementOperationResource> EffectiveOperations(AzureApiManagementApiResource api) =>
        api.Operations.Count > 0 || api.Backend is null || api.OpenApi is not null
            ? api.Operations
            : new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" }.Select(m =>
                new AzureApiManagementOperationResource($"{api.Name}-{m.ToLowerInvariant()}", $"proxy-{m.ToLowerInvariant()}", m, "/*", $"Proxy {m}", api)).ToList();

    /// <summary>Emits the <c>getApiManagementService</c> lookup (under the service's id, so <c>${id}.name/.id/.gatewayUrl</c> resolve as for a created service) and returns the resource group the service and its children live in.</summary>
    internal string AddExistingLookup(PulumiProgram program, string rg)
    {
        if (ExistingServiceResourceGroup is null && !HasConfiguredResourceGroup)
            throw new InvalidOperationException(
                $"'{Name}' is an existing API Management service but no resource group is known. Pass resourceGroupName to AsExisting/PublishAsExisting, or call WithExistingResourceGroup/WithResourceGroup.");
        rg = ExistingServiceResourceGroup ?? rg;
        program.Variables[Id] = new Dictionary<string, object?>
        {
            ["fn::invoke"] = new Dictionary<string, object?>
            {
                ["function"] = "azure-native:apimanagement:getApiManagementService",
                ["arguments"] = new Dictionary<string, object?> { ["resourceGroupName"] = rg, ["serviceName"] = ExistingServiceName },
            },
        };
        return rg;
    }

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        Contribute(program, rg, location);
        return Task.CompletedTask;
    }

    internal void Contribute(PulumiProgram program, string rg, string location)
    {
        if (ExistingServiceName is not null)
            rg = AddExistingLookup(program, rg);
        else
        {
        var options = Options ?? throw new InvalidOperationException(
            $"'{Name}' needs AzureApiManagementOptions (publisher email and name) unless it is an existing service; pass them to AddPulumiAzureApiManagement or call AsExisting.");
        // Named by the naming layer ({project}-{resource}-{stack}); override with WithAzureName.
        program.AddInfraResource(Id, "azure-native:apimanagement:ApiManagementService", new Dictionary<string, object?>
        {
            ["type"] = "azure-native:apimanagement:ApiManagementService",
            ["properties"] = new Dictionary<string, object?>
            {
                ["serviceName"] = NameFor(AzureNameKind.ApiManagementService),
                ["resourceGroupName"] = rg,
                ["location"] = location,
                ["publisherEmail"] = options.PublisherEmail,
                ["publisherName"] = options.PublisherName,
                ["sku"] = new Dictionary<string, object?> { ["name"] = options.Sku.ToString(), ["capacity"] = options.Capacity },
                ["identity"] = new Dictionary<string, object?> { ["type"] = "SystemAssigned" },
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
            },
        });

        program.Outputs[$"{Id}PrincipalId"] = $"${{{Id}.identity.principalId}}";
        }

        var service = $"${{{Id}.name}}";
        program.Outputs[$"{Id}GatewayUrl"] = $"${{{Id}.gatewayUrl}}";
        program.Outputs[$"{Id}Name"] = service;
        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";

        Dictionary<string, object?> Props(Dictionary<string, object?> extra)
        {
            var p = new Dictionary<string, object?> { ["resourceGroupName"] = rg, ["serviceName"] = service };
            foreach (var (k, v) in extra) p[k] = v;
            return p;
        }
        void Add(string id, string type, Dictionary<string, object?> props) =>
            program.AddInfraResource(id, type, new Dictionary<string, object?> { ["type"] = type, ["properties"] = props });
        void Policy(string id, string type, Dictionary<string, object?> props, string? xml)
        {
            if (xml is null) return;
            props["format"] = "xml";
            props["value"] = xml;
            Add(id, type, props);
        }

        Policy($"{Id}Policy", "azure-native:apimanagement:Policy", Props(new() { ["policyId"] = "policy" }),
            BuildPolicy(PolicyXml, InboundStatements));

        foreach (var nv in NamedValues)
            Add(Child(Id + "Nv", nv.Name), "azure-native:apimanagement:NamedValue", Props(new()
            {
                ["namedValueId"] = nv.NamedValueName,
                ["displayName"] = nv.NamedValueName,
                ["value"] = nv.Value,
                ["secret"] = nv.Secret,
            }));

        foreach (var backend in Backends)
            Add(Child(Id + "Backend", backend.Name), "azure-native:apimanagement:Backend", Props(new()
            {
                ["backendId"] = backend.BackendName,
                ["url"] = backend.Url,
                ["protocol"] = backend.Protocol,
                ["tls"] = new Dictionary<string, object?>
                {
                    ["validateCertificateChain"] = backend.ValidateCertificateChain,
                    ["validateCertificateName"] = backend.ValidateCertificateChain,
                },
            }));

        foreach (var api in Apis)
        {
            var apiId = Child(Id + "Api", api.Name);
            var apiRef = $"${{{apiId}.name}}";
            Add(apiId, "azure-native:apimanagement:Api", Props(ApiProperties(api)));

            var inbound = new List<string>(api.InboundStatements);
            if (api.Backend is { } b) inbound.Insert(0, $"<set-backend-service backend-id=\"{System.Security.SecurityElement.Escape(b.BackendName)}\" />");
            Policy($"{apiId}Policy", "azure-native:apimanagement:ApiPolicy", Props(new() { ["apiId"] = apiRef, ["policyId"] = "policy" }),
                BuildPolicy(api.PolicyXml, inbound));

            // A routed API without modeled operations forwards every method and path (a wildcard proxy).
            var operations = EffectiveOperations(api);
            foreach (var op in operations)
            {
                var opId = Child(apiId + "Op", op.Name);
                Add(opId, "azure-native:apimanagement:ApiOperation", Props(new()
                {
                    ["apiId"] = apiRef,
                    ["operationId"] = op.OperationName,
                    ["displayName"] = op.DisplayName,
                    ["method"] = op.Method,
                    ["urlTemplate"] = op.UrlTemplate,
                }));
                Policy($"{opId}Policy", "azure-native:apimanagement:ApiOperationPolicy",
                    Props(new() { ["apiId"] = apiRef, ["operationId"] = $"${{{opId}.name}}", ["policyId"] = "policy" }),
                    BuildPolicy(op.PolicyXml, op.InboundStatements));
            }
        }

        foreach (var product in Products)
        {
            var productId = Child(Id + "Product", product.Name);
            var props = Props(new()
            {
                ["productId"] = product.ProductName,
                ["displayName"] = product.DisplayName,
                ["state"] = product.State,
                ["subscriptionRequired"] = product.SubscriptionRequired,
            });
            if (product.SubscriptionRequired)
            {
                props["approvalRequired"] = product.ApprovalRequired;
                if (product.SubscriptionsLimit is { } limit) props["subscriptionsLimit"] = limit;
            }
            Add(productId, "azure-native:apimanagement:Product", props);
            foreach (var api in product.Apis)
                Add(Child(productId + "Api", api.Name), "azure-native:apimanagement:ProductApi", Props(new()
                {
                    ["productId"] = $"${{{productId}.name}}",
                    ["apiId"] = $"${{{Child(Id + "Api", api.Name)}.name}}",
                }));
        }

        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
    }
}
