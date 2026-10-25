using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Azure.Sql;

namespace Aspire.Hosting;

/// <summary>Pulumi counterparts of the extension methods in the official <c>Aspire.Hosting.Azure.Sql</c> package.</summary>
public static class AzureSqlExtensions
{
    /// <summary>Adds an Azure SQL server that the Pulumi stack provisions. Mirrors <c>AddAzureSqlServer</c>.</summary>
    public static IResourceBuilder<AzureSqlServerResource> AddPulumiAzureSqlServer(this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new AzureSqlServerResource(name)).ExcludeFromManifest();
    }

    /// <summary>Alias of <see cref="AddPulumiAzureSqlServer"/> following the <c>AddPulumiSql</c> naming of the issue.</summary>
    public static IResourceBuilder<AzureSqlServerResource> AddPulumiSql(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiAzureSqlServer(name);

    /// <summary>Adds a database to the server. <paramref name="databaseName"/> defaults to <paramref name="name"/>. Mirrors <c>AddDatabase</c>.</summary>
    public static IResourceBuilder<AzureSqlDatabaseResource> AddDatabase(
        this IResourceBuilder<AzureSqlServerResource> builder, string name, string? databaseName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (builder.Resource.Databases.Any(d => d.Name == name))
            throw new ArgumentException($"A database named '{name}' was already added to '{builder.Resource.Name}'.", nameof(name));
        var resource = new AzureSqlDatabaseResource(name, databaseName ?? name, builder.Resource);
        builder.Resource.Databases.Add(resource);
        return builder.ApplicationBuilder.AddResource(resource).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>
    /// Uses an existing server instead of creating one (no server, admin identity or role scripts are provisioned;
    /// firewall rules and databases are still applied to it). Mirrors <c>PublishAsExisting</c>.
    /// </summary>
    public static IResourceBuilder<AzureSqlServerResource> PublishAsExisting(
        this IResourceBuilder<AzureSqlServerResource> builder, string existingResourceName, string? resourceGroupName)
    {
        ArgumentException.ThrowIfNullOrEmpty(existingResourceName);
        ArgumentException.ThrowIfNullOrEmpty(resourceGroupName);
        builder.Resource.ExistingServerName = existingResourceName;
        builder.Resource.ExistingResourceGroup = resourceGroupName;
        return builder;
    }

    /// <summary>Alias of <see cref="PublishAsExisting"/> (this resource has no local run mode). Mirrors <c>AsExisting</c>.</summary>
    public static IResourceBuilder<AzureSqlServerResource> AsExisting(
        this IResourceBuilder<AzureSqlServerResource> builder, string existingResourceName, string? resourceGroupName) =>
        builder.PublishAsExisting(existingResourceName, resourceGroupName);

    /// <summary>Adds a server firewall rule (in addition to the default AllowAllAzureIps). Pulumi-specific convenience.</summary>
    public static IResourceBuilder<AzureSqlServerResource> WithFirewallRule(
        this IResourceBuilder<AzureSqlServerResource> builder, string name, string startIpAddress, string endIpAddress)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(startIpAddress);
        ArgumentException.ThrowIfNullOrEmpty(endIpAddress);
        builder.Resource.FirewallRules.Add((name, startIpAddress, endIpAddress));
        return builder;
    }

    /// <summary>Adds an elastic pool (default SKU GP_Gen5_2) that databases can join with <see cref="WithElasticPool"/>.</summary>
    public static IResourceBuilder<AzureSqlElasticPoolResource> AddElasticPool(
        this IResourceBuilder<AzureSqlServerResource> builder, string name, string? poolName = null, string sku = "GP_Gen5_2")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var resource = new AzureSqlElasticPoolResource(name, poolName ?? name, sku, builder.Resource);
        builder.Resource.ElasticPools.Add(resource);
        return builder.ApplicationBuilder.AddResource(resource).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>Places the database in an elastic pool of the same server instead of the free-tier serverless SKU.</summary>
    public static IResourceBuilder<AzureSqlDatabaseResource> WithElasticPool(
        this IResourceBuilder<AzureSqlDatabaseResource> builder, IResourceBuilder<AzureSqlElasticPoolResource> pool)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(pool);
        if (pool.Resource.Parent != builder.Resource.Parent)
            throw new ArgumentException("The elastic pool must belong to the same server as the database.", nameof(pool));
        builder.Resource.ElasticPool = pool.Resource;
        return builder;
    }
}
