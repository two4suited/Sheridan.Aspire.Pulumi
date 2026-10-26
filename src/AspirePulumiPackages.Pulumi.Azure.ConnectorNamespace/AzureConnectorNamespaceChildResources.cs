using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Azure.Base;

namespace AspirePulumiPackages.Pulumi.Azure.ConnectorNamespace;

/// <summary>A connection (<c>Microsoft.Web/connectorGateways/connections</c>) of a Connector Namespace.</summary>
public sealed class AzureConnectorNamespaceConnectionResource(
    string name, string connectionName, string connectorName, string? displayName, AzureConnectorNamespaceResource parent)
    : Resource(name), IResourceWithParent<AzureConnectorNamespaceResource>
{
    public string ConnectionName { get; } = connectionName;
    public string ConnectorName { get; } = connectorName;
    public string? DisplayName { get; } = displayName;
    public AzureConnectorNamespaceResource Parent { get; } = parent;
    internal List<AzureConnectorNamespaceConnectionAccessPolicyResource> AccessPolicies { get; } = [];
    internal bool IsExisting { get; set; }
}

/// <summary>An access policy on a connection. Either a literal principal or a Pulumi user-assigned identity.</summary>
internal sealed class AzureConnectorNamespaceConnectionAccessPolicyResource(
    string name, string policyName, AzureConnectorNamespaceConnectionResource parent,
    string? objectId, string? tenantId, PulumiAzureUserAssignedIdentityResource? identity) : Resource(name)
{
    public string PolicyName { get; } = policyName;
    public AzureConnectorNamespaceConnectionResource Parent { get; } = parent;
    public string? ObjectId { get; } = objectId;
    public string? TenantId { get; } = tenantId;
    public PulumiAzureUserAssignedIdentityResource? Identity { get; } = identity;
}

/// <summary>An MCP server configuration (<c>Microsoft.Web/connectorGateways/mcpserverConfigs</c>).</summary>
public sealed class AzureConnectorNamespaceMcpServerConfigResource(
    string name, string configName, string? description, AzureConnectorNamespaceResource parent)
    : Resource(name), IResourceWithParent<AzureConnectorNamespaceResource>
{
    public string ConfigName { get; } = configName;
    public string? Description { get; } = description;
    public AzureConnectorNamespaceResource Parent { get; } = parent;
    internal List<AzureConnectorNamespaceMcpConnectorDefinition> Connectors { get; } = [];
    internal List<AzureConnectorNamespaceMcpAccessPolicyResource> AccessPolicies { get; } = [];
    internal bool IsExisting { get; set; }
}

internal sealed record AzureConnectorNamespaceMcpOperationDefinition(string Name, string? DisplayName, string? Description);

internal sealed class AzureConnectorNamespaceMcpConnectorDefinition(
    string name, string? displayName, string? description, AzureConnectorNamespaceConnectionResource connection)
{
    public string Name { get; } = name;
    public string? DisplayName { get; } = displayName;
    public string? Description { get; } = description;
    public AzureConnectorNamespaceConnectionResource Connection { get; } = connection;
    public List<AzureConnectorNamespaceMcpOperationDefinition> Operations { get; } = [];
}

internal sealed class AzureConnectorNamespaceMcpAccessPolicyResource(
    string name, AzureConnectorNamespaceMcpServerConfigResource parent, string objectId, string tenantId,
    AzureConnectorNamespaceMcpAccessPolicyPrincipalType principalType) : Resource(name)
{
    public AzureConnectorNamespaceMcpServerConfigResource Parent { get; } = parent;
    public string ObjectId { get; } = objectId;
    public string TenantId { get; } = tenantId;
    public AzureConnectorNamespaceMcpAccessPolicyPrincipalType PrincipalType { get; } = principalType;
}
