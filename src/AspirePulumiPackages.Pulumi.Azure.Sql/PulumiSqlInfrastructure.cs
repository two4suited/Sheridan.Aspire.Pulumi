using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.Sql;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi SQL server. Port of the Azure.Provisioning
/// Sql overlay (root: <c>SqlServer</c>). Properties use azure-native names, e.g.
/// <c>SqlServer.Set("minimalTlsVersion", "1.2")</c>.
/// </summary>
public sealed class PulumiSqlInfrastructure(PulumiInfrastructure inner, AzureSqlServerResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:sql:Server</c>. Throws for servers declared with PublishAsExisting.</summary>
    public PulumiResourceNode SqlServer => owner.ExistingServerName is null
        ? GetResource(owner.ServerId)
        : throw new InvalidOperationException($"'{owner.Name}' is an existing server; there is no Server resource to configure.");

    /// <summary>The databases (<c>azure-native:sql:Database</c>) of this server.</summary>
    public IEnumerable<PulumiResourceNode> Databases => owner.Databases.Select(d => GetResource($"{owner.Id}{d.Id}"));
}

public static class PulumiSqlInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the SQL resources before the program is emitted.</summary>
    public static IResourceBuilder<AzureSqlServerResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureSqlServerResource> builder, Action<PulumiSqlInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiSqlInfrastructure(infra, owner)));
    }
}
