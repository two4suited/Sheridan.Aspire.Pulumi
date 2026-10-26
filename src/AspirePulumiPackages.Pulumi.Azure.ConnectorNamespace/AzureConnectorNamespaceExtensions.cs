using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Azure.Base;
using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.ConnectorNamespace;

/// <summary>Pulumi equivalents of the Aspire.Hosting.Azure.ConnectorNamespace extension methods.</summary>
public static class AzureConnectorNamespaceExtensions
{
    /// <summary>Adds an Azure Connector Namespace provisioned by the Pulumi stack (the equivalent of AddAzureConnectorNamespace).</summary>
    public static IResourceBuilder<AzureConnectorNamespaceResource> AddPulumiConnectorNamespace(
        this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return builder.AddResource(new AzureConnectorNamespaceResource(name)).ExcludeFromManifest();
    }

    /// <summary>Alias of <see cref="AddPulumiConnectorNamespace"/> following the <c>AddPulumiAzureX</c> naming.</summary>
    public static IResourceBuilder<AzureConnectorNamespaceResource> AddPulumiAzureConnectorNamespace(
        this IDistributedApplicationBuilder builder, string name) => builder.AddPulumiConnectorNamespace(name);

    /// <summary>Adds a connection to a connector (e.g. <c>office365</c>) on the namespace.</summary>
    public static IResourceBuilder<AzureConnectorNamespaceConnectionResource> AddConnection(
        this IResourceBuilder<AzureConnectorNamespaceResource> builder, string name, string connectorName,
        AzureConnectorNamespaceConnectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorName);
        var connectionName = options?.ConnectionName ?? name;
        ValidateConnectorResourceName(connectionName, nameof(options));
        if (builder.Resource.Connections.Any(c => string.Equals(c.ConnectionName, connectionName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Connector connection '{connectionName}' is already registered on Connector Namespace '{builder.Resource.Name}'.");

        var connection = new AzureConnectorNamespaceConnectionResource(name, connectionName, connectorName, options?.DisplayName, builder.Resource);
        builder.Resource.Connections.Add(connection);
        return builder.ApplicationBuilder.AddResource(connection).ExcludeFromManifest();
    }

    /// <summary>
    /// Injects <c>{name}__connectorGatewayName</c> (the generated namespace name) and <c>{name}__connectionName</c> into the destination.
    /// </summary>
    public static IResourceBuilder<TDestination> WithReference<TDestination>(
        this IResourceBuilder<TDestination> builder, IResourceBuilder<AzureConnectorNamespaceConnectionResource> connection, string? connectionName = null)
        where TDestination : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(connection);
        if (connectionName is not null) ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        var configurationName = connectionName ?? connection.Resource.Name;
        var conn = connection.Resource;
        return builder
            .WithReferenceRelationship(conn)
            .WithEnvironment($"{configurationName}__connectorGatewayName", conn.Parent.NameOutputReference)
            .WithEnvironment($"{configurationName}__connectionName", conn.ConnectionName);
    }

    /// <summary>Marks the connection as pre-existing: it is referenced but not created.</summary>
    public static IResourceBuilder<AzureConnectorNamespaceConnectionResource> AsExisting(
        this IResourceBuilder<AzureConnectorNamespaceConnectionResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!string.IsNullOrWhiteSpace(builder.Resource.DisplayName))
            throw new InvalidOperationException($"Connector connection '{builder.Resource.Name}' configures a display name and cannot be marked as existing.");
        if (builder.Resource.AccessPolicies.Count > 0)
            throw new InvalidOperationException($"Connector connection '{builder.Resource.Name}' configures access policies and cannot be marked as existing.");
        builder.Resource.IsExisting = true;
        return builder;
    }

    /// <summary>Grants an Entra principal (object id + tenant id) access to the connection.</summary>
    public static IResourceBuilder<AzureConnectorNamespaceConnectionResource> WithAccessPolicy(
        this IResourceBuilder<AzureConnectorNamespaceConnectionResource> builder, string name, AzureConnectorNamespaceAccessPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(options);
        ValidateEntraPrincipalIds(options.ObjectId, options.TenantId, "connection access policy");
        EnsureMutable(builder.Resource);
        var policyName = options.PolicyName ?? name;
        ValidateConnectorResourceName(policyName, nameof(options));
        AddConnectionPolicy(builder.Resource, new AzureConnectorNamespaceConnectionAccessPolicyResource(
            name, policyName, builder.Resource, options.ObjectId, options.TenantId, identity: null));
        return builder;
    }

    /// <summary>
    /// Grants a Pulumi-provisioned user-assigned identity access to the connection. The identity must be deployed by the
    /// same Pulumi stack as the namespace (its principal id is referenced directly in the program).
    /// </summary>
    public static IResourceBuilder<AzureConnectorNamespaceConnectionResource> WithIdentityAccessPolicy(
        this IResourceBuilder<AzureConnectorNamespaceConnectionResource> builder, string name,
        IResourceBuilder<PulumiAzureUserAssignedIdentityResource> identity, string? policyName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(identity);
        EnsureMutable(builder.Resource);
        policyName ??= name;
        ValidateConnectorResourceName(policyName, nameof(policyName));
        AddConnectionPolicy(builder.Resource, new AzureConnectorNamespaceConnectionAccessPolicyResource(
            name, policyName, builder.Resource, objectId: null, tenantId: null, identity.Resource));
        return builder;
    }

    /// <summary>Adds a managed MCP server configuration to the namespace.</summary>
    public static IResourceBuilder<AzureConnectorNamespaceMcpServerConfigResource> AddMcpServerConfig(
        this IResourceBuilder<AzureConnectorNamespaceResource> builder, string name, AzureConnectorNamespaceMcpServerConfigOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var configName = options?.ConfigName ?? name;
        ValidateConnectorResourceName(configName, nameof(options));
        if (builder.Resource.McpServerConfigs.Any(c => string.Equals(c.ConfigName, configName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"MCP server configuration '{configName}' is already registered on Connector Namespace '{builder.Resource.Name}'.");

        var config = new AzureConnectorNamespaceMcpServerConfigResource(name, configName, options?.Description, builder.Resource);
        builder.Resource.McpServerConfigs.Add(config);
        return builder.ApplicationBuilder.AddResource(config).ExcludeFromManifest();
    }

    /// <summary>Marks the MCP server configuration as pre-existing: it is not created.</summary>
    public static IResourceBuilder<AzureConnectorNamespaceMcpServerConfigResource> AsExisting(
        this IResourceBuilder<AzureConnectorNamespaceMcpServerConfigResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var r = builder.Resource;
        if (r.Connectors.Count > 0) throw new InvalidOperationException($"MCP server configuration '{r.Name}' has connector routes and cannot be marked as existing.");
        if (!string.IsNullOrWhiteSpace(r.Description)) throw new InvalidOperationException($"MCP server configuration '{r.Name}' configures a description and cannot be marked as existing.");
        if (r.AccessPolicies.Count > 0) throw new InvalidOperationException($"MCP server configuration '{r.Name}' configures access policies and cannot be marked as existing.");
        r.IsExisting = true;
        return builder;
    }

    /// <summary>Grants an Entra user or group access to the MCP server configuration.</summary>
    public static IResourceBuilder<AzureConnectorNamespaceMcpServerConfigResource> WithAccessPolicy(
        this IResourceBuilder<AzureConnectorNamespaceMcpServerConfigResource> builder, string name, AzureConnectorNamespaceMcpAccessPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(options);
        ValidateEntraPrincipalIds(options.ObjectId, options.TenantId, "MCP access policy");
        if (!Enum.IsDefined(options.PrincipalType))
            throw new ArgumentException($"'{options.PrincipalType}' is not a supported MCP access policy principal type.", nameof(options));
        if (builder.Resource.IsExisting)
            throw new InvalidOperationException($"Existing MCP server configuration '{builder.Resource.Name}' is read-only and cannot create an access policy.");
        if (builder.Resource.AccessPolicies.Any(p => string.Equals(p.ObjectId, options.ObjectId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"An access policy for principal '{options.ObjectId}' is already registered on MCP server configuration '{builder.Resource.Name}'.");
        builder.Resource.AccessPolicies.Add(new AzureConnectorNamespaceMcpAccessPolicyResource(
            name, builder.Resource, options.ObjectId, options.TenantId, options.PrincipalType));
        return builder;
    }

    /// <summary>Routes the MCP server to a connector on <paramref name="connection"/>, allow-listing the given operations.</summary>
    public static IResourceBuilder<AzureConnectorNamespaceMcpServerConfigResource> WithConnector(
        this IResourceBuilder<AzureConnectorNamespaceMcpServerConfigResource> builder, string connectorName,
        IResourceBuilder<AzureConnectorNamespaceConnectionResource> connection, AzureConnectorNamespaceMcpConnectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorName);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        if (builder.Resource.IsExisting) throw new InvalidOperationException($"Existing MCP server configuration '{builder.Resource.Name}' is read-only.");
        if (!ReferenceEquals(builder.Resource.Parent, connection.Resource.Parent))
            throw new InvalidOperationException($"Connector connection '{connection.Resource.Name}' belongs to a different Connector Namespace.");
        if (builder.Resource.Connectors.Count > 0)
            throw new InvalidOperationException($"MCP server configuration '{builder.Resource.Name}' already has a connector. The current Connector Namespace preview supports one connector per MCP server configuration.");
        if (options.Operations is null || options.Operations.Length == 0)
            throw new ArgumentException("At least one connector operation must be explicitly allow-listed.", nameof(options));

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var definition = new AzureConnectorNamespaceMcpConnectorDefinition(connectorName, options.DisplayName, options.Description, connection.Resource);
        foreach (var op in options.Operations)
        {
            ArgumentNullException.ThrowIfNull(op);
            ArgumentException.ThrowIfNullOrWhiteSpace(op.Name);
            if (!names.Add(op.Name)) throw new ArgumentException($"Connector operation '{op.Name}' is configured more than once.", nameof(options));
            definition.Operations.Add(new AzureConnectorNamespaceMcpOperationDefinition(op.Name, op.DisplayName, op.Description));
        }
        builder.Resource.Connectors.Add(definition);
        return builder;
    }

    /// <summary>Pulumi equivalent of <c>ConfigureInfrastructure</c>: customize the namespace's deployment before the program is emitted.</summary>
    public static IResourceBuilder<AzureConnectorNamespaceResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureConnectorNamespaceResource> builder, Action<PulumiConnectorNamespaceInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiConnectorNamespaceInfrastructure(infra, owner)));
    }

    private static void EnsureMutable(AzureConnectorNamespaceConnectionResource connection)
    {
        if (connection.IsExisting)
            throw new InvalidOperationException($"Existing connector connection '{connection.Name}' is read-only and cannot create an access policy.");
    }

    private static void AddConnectionPolicy(AzureConnectorNamespaceConnectionResource connection, AzureConnectorNamespaceConnectionAccessPolicyResource policy)
    {
        if (connection.AccessPolicies.Any(p => string.Equals(p.PolicyName, policy.PolicyName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Access policy '{policy.PolicyName}' is already registered on connector connection '{connection.Name}'.");
        connection.AccessPolicies.Add(policy);
    }

    private static void ValidateEntraPrincipalIds(string objectId, string tenantId, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (!Guid.TryParse(objectId, out _)) throw new ArgumentException($"The {description} object ID must be a valid GUID.");
        if (!Guid.TryParse(tenantId, out _)) throw new ArgumentException($"The {description} tenant ID must be a valid GUID.");
    }

    private static void ValidateConnectorResourceName(string name, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name, paramName);
        if (name.Length is < 2 or > 64)
            throw new ArgumentOutOfRangeException(paramName, name, "Connector Namespace resource names must contain between 2 and 64 characters.");
        if (name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Connector Namespace resource names can contain only ASCII letters, numbers, hyphens, and underscores.", paramName);
    }
}
