using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Azure.PostgreSQL;

namespace Aspire.Hosting;

/// <summary>Pulumi counterparts of the extension methods in the official <c>Aspire.Hosting.Azure.PostgreSQL</c> package.</summary>
public static class AzurePostgresExtensions
{
    /// <summary>
    /// Adds an Azure Database for PostgreSQL flexible server that the Pulumi stack provisions. Mirrors
    /// <c>AddAzurePostgresFlexibleServer</c>; Microsoft Entra authentication is used unless <see cref="WithPasswordAuthentication"/> is called.
    /// </summary>
    public static IResourceBuilder<AzurePostgresFlexibleServerResource> AddPulumiAzurePostgresFlexibleServer(
        this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new AzurePostgresFlexibleServerResource(name)).ExcludeFromManifest();
    }

    /// <summary>Alias of <see cref="AddPulumiAzurePostgresFlexibleServer"/>.</summary>
    public static IResourceBuilder<AzurePostgresFlexibleServerResource> AddPulumiPostgreSQL(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiAzurePostgresFlexibleServer(name);

    /// <summary>
    /// Uses password authentication (and disables Microsoft Entra authentication). The administrator login and password come
    /// from the given parameters, which become Pulumi config entries; when omitted, config entries
    /// <c>&lt;server&gt;AdministratorLogin</c> (default <c>pgadmin</c>) and a secret <c>&lt;server&gt;AdministratorPassword</c> are declared.
    /// The connection strings are also stored in a Key Vault created for the server. Mirrors <c>WithPasswordAuthentication</c>.
    /// </summary>
    public static IResourceBuilder<AzurePostgresFlexibleServerResource> WithPasswordAuthentication(
        this IResourceBuilder<AzurePostgresFlexibleServerResource> builder,
        IResourceBuilder<ParameterResource>? userName = null,
        IResourceBuilder<ParameterResource>? password = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Resource.UsePasswordAuthentication = true;
        builder.Resource.UserNameParameter = userName?.Resource;
        builder.Resource.PasswordParameter = password?.Resource;
        return builder;
    }

    /// <summary>Adds a database to the server (<paramref name="databaseName"/> defaults to <paramref name="name"/>). Mirrors <c>AddDatabase</c>.</summary>
    public static IResourceBuilder<AzurePostgresFlexibleServerDatabaseResource> AddDatabase(
        this IResourceBuilder<AzurePostgresFlexibleServerResource> builder, string name, string? databaseName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (builder.Resource.Databases.Any(d => d.Name == name))
            throw new ArgumentException($"A database named '{name}' was already added to '{builder.Resource.Name}'.", nameof(name));

        var resource = new AzurePostgresFlexibleServerDatabaseResource(name, databaseName ?? name, builder.Resource);
        builder.Resource.Databases.Add(resource);

        if (builder.Resource.InnerResource is { } inner)
            resource.InnerResource = builder.ApplicationBuilder.CreateResourceBuilder(inner).AddDatabase($"{name}-container", resource.DatabaseName).Resource;

        return builder.ApplicationBuilder.AddResource(resource).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>
    /// Runs a local PostgreSQL container instead of connecting to the Pulumi-provisioned server. No effect in publish mode.
    /// Mirrors <c>RunAsContainer</c>.
    /// </summary>
    public static IResourceBuilder<AzurePostgresFlexibleServerResource> RunAsContainer(
        this IResourceBuilder<AzurePostgresFlexibleServerResource> builder,
        Action<IResourceBuilder<PostgresServerResource>>? configureContainer = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.ApplicationBuilder.ExecutionContext.IsPublishMode) return builder;
        if (builder.Resource.InnerResource is not null)
            throw new InvalidOperationException($"'{builder.Resource.Name}' already runs as a container.");

        var container = builder.ApplicationBuilder.AddPostgres($"{builder.Resource.Name}-container")
            .WithParentRelationship(builder)
            .ExcludeFromManifest();
        builder.Resource.InnerResource = container.Resource;

        foreach (var db in builder.Resource.Databases)
            db.InnerResource = container.AddDatabase($"{db.Name}-container", db.DatabaseName).Resource;

        configureContainer?.Invoke(container);
        return builder;
    }
}
