namespace AspirePulumiPackages.Pulumi.Azure.ConnectorNamespace;

/// <summary>Options for <c>AddConnection</c> (mirrors the official <c>AzureConnectorNamespaceConnectionOptions</c>).</summary>
public sealed class AzureConnectorNamespaceConnectionOptions
{
    /// <summary>The Azure connection name; defaults to the Aspire resource name.</summary>
    public string? ConnectionName { get; set; }
    /// <summary>The connection display name; defaults to the connection name.</summary>
    public string? DisplayName { get; set; }
}

/// <summary>Options for a connection access policy.</summary>
public sealed class AzureConnectorNamespaceAccessPolicyOptions
{
    public string? PolicyName { get; set; }
    public required string ObjectId { get; set; }
    public required string TenantId { get; set; }
}

public enum AzureConnectorNamespaceMcpAccessPolicyPrincipalType
{
    User = 1,
    Group = 2,
}

/// <summary>Options for an MCP server configuration access policy.</summary>
public sealed class AzureConnectorNamespaceMcpAccessPolicyOptions
{
    public required string ObjectId { get; set; }
    public required string TenantId { get; set; }
    public required AzureConnectorNamespaceMcpAccessPolicyPrincipalType PrincipalType { get; set; }
}

public sealed class AzureConnectorNamespaceMcpServerConfigOptions
{
    public string? ConfigName { get; set; }
    public string? Description { get; set; }
}

public sealed class AzureConnectorNamespaceMcpConnectorOptions
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public AzureConnectorNamespaceMcpOperationOptions[] Operations { get; set; } = [];
}

public sealed class AzureConnectorNamespaceMcpOperationOptions
{
    public required string Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
}
