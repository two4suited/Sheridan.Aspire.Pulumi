using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.PostgreSQL;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi PostgreSQL flexible server. Port of the Azure.Provisioning
/// PostgreSql overlay (root: <c>PostgreSqlFlexibleServer</c>). Properties use azure-native names, e.g.
/// <c>PostgreSqlFlexibleServer.Set("sku.name", "Standard_D2s_v3")</c>.
/// </summary>
public sealed class PulumiPostgresInfrastructure(PulumiInfrastructure inner, AzurePostgresFlexibleServerResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:dbforpostgresql:Server</c>.</summary>
    public PulumiResourceNode PostgreSqlFlexibleServer => GetResource(owner.Id);

    /// <summary>The firewall rules (<c>azure-native:dbforpostgresql:FirewallRule</c>) of this server.</summary>
    public IEnumerable<PulumiResourceNode> FirewallRules =>
        GetResources("azure-native:dbforpostgresql:FirewallRule").Where(r => r.Id.StartsWith(owner.Id, StringComparison.Ordinal));

    /// <summary>The databases (<c>azure-native:dbforpostgresql:Database</c>) of this server.</summary>
    public IEnumerable<PulumiResourceNode> Databases =>
        GetResources("azure-native:dbforpostgresql:Database").Where(r => r.Id.StartsWith(owner.Id, StringComparison.Ordinal));
}

public static class PulumiPostgresInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the PostgreSQL resources before the program is emitted.</summary>
    public static IResourceBuilder<AzurePostgresFlexibleServerResource> ConfigureInfrastructure(
        this IResourceBuilder<AzurePostgresFlexibleServerResource> builder, Action<PulumiPostgresInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiPostgresInfrastructure(infra, owner)));
    }
}
