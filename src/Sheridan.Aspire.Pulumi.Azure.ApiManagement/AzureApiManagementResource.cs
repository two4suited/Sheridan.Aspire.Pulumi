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
public sealed class AzureApiManagementResource(string name, AzureApiManagementOptions options) : PulumiAzureResource(name)
{
    public AzureApiManagementOptions Options { get; } = options;

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
    public PulumiOutputReference PrincipalId => OutputRef("PrincipalId");

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

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        // Named by the naming layer ({project}-{resource}-{stack}); override with WithAzureName.
        program.AddInfraResource(Id, "azure-native:apimanagement:ApiManagementService", new Dictionary<string, object?>
        {
            ["type"] = "azure-native:apimanagement:ApiManagementService",
            ["properties"] = new Dictionary<string, object?>
            {
                ["serviceName"] = NameFor(AzureNameKind.ApiManagementService),
                ["resourceGroupName"] = rg,
                ["location"] = location,
                ["publisherEmail"] = Options.PublisherEmail,
                ["publisherName"] = Options.PublisherName,
                ["sku"] = new Dictionary<string, object?> { ["name"] = Options.Sku.ToString(), ["capacity"] = Options.Capacity },
                ["identity"] = new Dictionary<string, object?> { ["type"] = "SystemAssigned" },
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
            },
        });

        var service = $"${{{Id}.name}}";
        program.Outputs[$"{Id}GatewayUrl"] = $"${{{Id}.gatewayUrl}}";
        program.Outputs[$"{Id}Name"] = service;
        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";
        program.Outputs[$"{Id}PrincipalId"] = $"${{{Id}.identity.principalId}}";

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
            Add(apiId, "azure-native:apimanagement:Api", Props(new()
            {
                ["apiId"] = api.ApiName,
                ["path"] = api.Path,
                ["displayName"] = api.DisplayName,
                ["protocols"] = new List<object?> { "https" },
                ["subscriptionRequired"] = api.SubscriptionRequired,
            }));

            var inbound = new List<string>(api.InboundStatements);
            if (api.Backend is { } b) inbound.Insert(0, $"<set-backend-service backend-id=\"{System.Security.SecurityElement.Escape(b.BackendName)}\" />");
            Policy($"{apiId}Policy", "azure-native:apimanagement:ApiPolicy", Props(new() { ["apiId"] = apiRef, ["policyId"] = "policy" }),
                BuildPolicy(api.PolicyXml, inbound));

            // A routed API without modeled operations forwards every method and path (a wildcard proxy).
            var operations = api.Operations.Count > 0 || api.Backend is null
                ? api.Operations
                : new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" }.Select(m =>
                    new AzureApiManagementOperationResource($"{api.Name}-{m.ToLowerInvariant()}", $"proxy-{m.ToLowerInvariant()}", m, "/*", $"Proxy {m}", api)).ToList();
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
        return Task.CompletedTask;
    }
}
