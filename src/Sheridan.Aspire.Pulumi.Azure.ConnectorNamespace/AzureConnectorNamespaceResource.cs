using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Sheridan.Aspire.Pulumi.Azure.Base;

namespace Sheridan.Aspire.Pulumi.Azure.ConnectorNamespace;

/// <summary>
/// An Azure Connector Namespace provisioned by Pulumi, mirroring Aspire.Hosting.Azure.ConnectorNamespace's Bicep:
/// a <c>Microsoft.Web/connectorGateways</c> (system-assigned identity) with connections, connection access policies,
/// MCP server configurations and their access policies.
/// <para>
/// The azure-native provider has no <c>connectorGateways</c> resource (it is a preview ARM contract without published
/// metadata), so the whole namespace is deployed as one <c>azure-native:resources:Deployment</c> (an ARM template
/// deployment of the exact resource types and <c>2026-05-01-preview</c> API version the official Bicep uses).
/// </para>
/// </summary>
public sealed class AzureConnectorNamespaceResource(string name) : PulumiAzureResource(name)
{
    internal const string ApiVersion = "2026-05-01-preview";
    private const string GatewayType = "Microsoft.Web/connectorGateways";

    /// <summary>Name of an existing gateway (set by PublishAsExisting/AsExisting); when set the gateway itself is not created.</summary>
    public string? ExistingGatewayName { get; internal set; }

    internal List<AzureConnectorNamespaceConnectionResource> Connections { get; } = [];
    internal List<AzureConnectorNamespaceMcpServerConfigResource> McpServerConfigs { get; } = [];

    internal PulumiOutputReference OutputRef(string suffix) =>
        Output(Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack()."), suffix);

    /// <summary>The generated gateway name (composed from the app, resource and stack names; see docs/naming.md).</summary>
    public PulumiOutputReference NameOutputReference => OutputRef("Name");
    public PulumiOutputReference IdOutputReference => OutputRef("Id");
    /// <summary>Principal id of the gateway's system-assigned identity.</summary>
    public PulumiOutputReference PrincipalId => OutputRef("PrincipalId");
    public PulumiOutputReference TenantId => OutputRef("TenantId");

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        const string gw = "variables('gatewayName')";
        string Child(string type, params string[] names) =>
            $"[resourceId('{GatewayType}/{type}', {gw}, {string.Join(", ", names.Select(n => $"'{n}'"))})]";
        var existing = ExistingGatewayName;
        var gatewayDependency = existing is null ? $"[resourceId('{GatewayType}', {gw})]" : null;
        // An existing gateway is not part of the template, so children must not dependOn it.
        List<object?> GatewayDependsOn() => gatewayDependency is null ? [] : [gatewayDependency];

        var resources = new List<object?>();
        if (existing is null)
        {
            resources.Add(new Dictionary<string, object?>
            {
                ["type"] = GatewayType,
                ["apiVersion"] = ApiVersion,
                ["name"] = $"[{gw}]",
                ["location"] = "[resourceGroup().location]",
                ["identity"] = new Dictionary<string, object?> { ["type"] = "SystemAssigned" },
                ["properties"] = new Dictionary<string, object?>(),
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
            });
        }

        foreach (var connection in Connections.Where(c => !c.IsExisting))
        {
            var connectionNode = new Dictionary<string, object?>
            {
                ["type"] = $"{GatewayType}/connections",
                ["apiVersion"] = ApiVersion,
                ["name"] = $"[concat({gw}, '/{connection.ConnectionName}')]",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["displayName"] = connection.DisplayName ?? connection.ConnectionName,
                    ["connectorName"] = connection.ConnectorName,
                },
            };
            if (GatewayDependsOn() is { Count: > 0 } connectionDeps) connectionNode["dependsOn"] = connectionDeps;
            resources.Add(connectionNode);

            foreach (var policy in connection.AccessPolicies)
            {
                var objectId = policy.Identity is { } id ? $"${{{id.Id}.principalId}}" : policy.ObjectId;
                var tenantId = policy.Identity is not null ? "[tenant().tenantId]" : policy.TenantId;
                resources.Add(new Dictionary<string, object?>
                {
                    ["type"] = $"{GatewayType}/connections/accessPolicies",
                    ["apiVersion"] = ApiVersion,
                    ["name"] = $"[concat({gw}, '/{connection.ConnectionName}/{policy.PolicyName}')]",
                    ["location"] = "[resourceGroup().location]",
                    ["dependsOn"] = new List<object?> { Child("connections", connection.ConnectionName) },
                    ["properties"] = Principal(objectId, tenantId),
                });
            }
        }

        foreach (var config in McpServerConfigs)
        {
            if (!config.IsExisting && config.Connectors.Count == 0)
                throw new InvalidOperationException($"MCP server configuration '{config.Name}' requires a connector. Call WithConnector before generating the Azure deployment.");
            if (!config.IsExisting && config.AccessPolicies.Count == 0)
                throw new InvalidOperationException($"MCP server configuration '{config.Name}' requires an access policy. Call WithAccessPolicy before generating the Azure deployment.");
            if (config.IsExisting) continue;

            var connectors = new List<object?>();
            var dependsOn = GatewayDependsOn();
            foreach (var connector in config.Connectors)
            {
                if (!connector.Connection.IsExisting)
                    dependsOn.Add(Child("connections", connector.Connection.ConnectionName));
                var connectorNode = new Dictionary<string, object?>
                {
                    ["name"] = connector.Name,
                    ["connectionName"] = connector.Connection.ConnectionName,
                    ["displayName"] = connector.DisplayName ?? connector.Name,
                };
                if (!string.IsNullOrWhiteSpace(connector.Description)) connectorNode["description"] = connector.Description;
                connectorNode["operations"] = connector.Operations.Select(o =>
                {
                    var op = new Dictionary<string, object?> { ["name"] = o.Name, ["displayName"] = o.DisplayName ?? o.Name };
                    if (!string.IsNullOrWhiteSpace(o.Description)) op["description"] = o.Description;
                    return (object?)op;
                }).ToList();
                connectors.Add(connectorNode);
            }

            var configProps = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(config.Description)) configProps["description"] = config.Description;
            configProps["state"] = "Enabled";
            configProps["connectors"] = connectors;
            var configNode = new Dictionary<string, object?>
            {
                ["type"] = $"{GatewayType}/mcpserverConfigs",
                ["apiVersion"] = ApiVersion,
                ["name"] = $"[concat({gw}, '/{config.ConfigName}')]",
                ["kind"] = "ManagedMcpServer",
            };
            if (dependsOn.Count > 0) configNode["dependsOn"] = dependsOn;
            configNode["properties"] = configProps;
            resources.Add(configNode);

            foreach (var policy in config.AccessPolicies)
            {
                var props = Principal(policy.ObjectId, policy.TenantId);
                props["principalType"] = policy.PrincipalType.ToString();
                resources.Add(new Dictionary<string, object?>
                {
                    ["type"] = $"{GatewayType}/mcpserverConfigs/accessPolicies",
                    ["apiVersion"] = ApiVersion,
                    // The service requires the access-policy name to match the principal object id.
                    ["name"] = $"[concat({gw}, '/{config.ConfigName}/{policy.ObjectId}')]",
                    ["location"] = "[resourceGroup().location]",
                    ["dependsOn"] = new List<object?> { Child("mcpserverConfigs", config.ConfigName) },
                    ["properties"] = props,
                });
            }
        }

        var gwRef = $"reference(resourceId('{GatewayType}', {gw}), '{ApiVersion}', 'Full')";
        program.AddInfraResource(Id, "azure-native:resources:Deployment", new Dictionary<string, object?>
        {
            ["type"] = "azure-native:resources:Deployment",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = rg,
                ["deploymentName"] = $"{Name}-connector-namespace",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["mode"] = "Incremental",
                    ["template"] = new Dictionary<string, object?>
                    {
                        ["$schema"] = "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#",
                        ["contentVersion"] = "1.0.0.0",
                        ["variables"] = new Dictionary<string, object?>
                        {
                            ["gatewayName"] = existing ?? NameFor(AzureNameKind.ConnectorGateway),
                        },
                        ["resources"] = resources,
                        ["outputs"] = new Dictionary<string, object?>
                        {
                            ["id"] = Out($"resourceId('{GatewayType}', {gw})"),
                            ["name"] = Out(gw),
                            ["principalId"] = Out($"{gwRef}.identity.principalId"),
                            ["tenantId"] = Out($"{gwRef}.identity.tenantId"),
                        },
                    },
                },
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
            },
        });

        program.Outputs[$"{Id}Id"] = $"${{{Id}.properties.outputs.id.value}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.properties.outputs.name.value}}";
        program.Outputs[$"{Id}PrincipalId"] = $"${{{Id}.properties.outputs.principalId.value}}";
        program.Outputs[$"{Id}TenantId"] = $"${{{Id}.properties.outputs.tenantId.value}}";

        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }

    private static Dictionary<string, object?> Principal(string? objectId, string? tenantId) => new()
    {
        ["principal"] = new Dictionary<string, object?>
        {
            ["type"] = "ActiveDirectory",
            ["identity"] = new Dictionary<string, object?> { ["objectId"] = objectId, ["tenantId"] = tenantId },
        },
    };

    private static Dictionary<string, object?> Out(string expression) =>
        new() { ["type"] = "string", ["value"] = $"[{expression}]" };
}
