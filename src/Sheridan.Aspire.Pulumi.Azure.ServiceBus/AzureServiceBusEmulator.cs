using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Sheridan.Aspire.Pulumi.Azure.ServiceBus;

/// <summary>The Azure Service Bus emulator container added by <c>RunAsEmulator</c> (run mode only).</summary>
public sealed class AzureServiceBusEmulatorResource(AzureServiceBusResource innerResource)
    : ContainerResource(innerResource.Name + "-emulator")
{
    internal const string EndpointName = "emulator";
    internal const string HealthEndpointName = "emulator-health";
    internal const string ConfigFilesPath = "/ServiceBus_Emulator/ConfigFiles";
    internal const string ConfigFile = "Config.json";

    public AzureServiceBusResource InnerResource { get; } = innerResource;

    /// <summary>The SQL Server container that stores the emulator's state.</summary>
    public ContainerResource? SqlServer { get; internal set; }

    internal SqlServerServerResource? SqlServerResource => SqlServer as SqlServerServerResource;
    internal IResourceBuilder<SqlServerServerResource>? ExistingSqlServer { get; set; }
    internal List<Action<IResourceBuilder<SqlServerServerResource>>> SqlServerConfigurations { get; } = [];

    internal string? ConfigurationFilePath { get; set; }
    internal List<Action<JsonNode>> ConfigurationCallbacks { get; } = [];
}

public static class AzureServiceBusEmulatorExtensions
{
    internal const string EmulatorImage = "azure-messaging/servicebus-emulator";
    internal const string EmulatorTag = "2.0.0";
    private const string SqlServerEndpointName = "tcp";
    private const string SqlServerConflictMessage = "The Azure Service Bus emulator cannot use both an existing SQL Server resource and a customized built-in SQL Server resource. Remove either the 'WithSqlServer(sqlServer)' call or the 'WithSqlServer(configureSqlServer)' call.";

    /// <summary>
    /// Runs the Azure Service Bus emulator (and the SQL Server container it needs) locally instead of connecting to the
    /// Pulumi-provisioned namespace. The queues, topics, subscriptions and rules declared on the resource are written to the
    /// emulator's <c>Config.json</c>. No effect in publish mode. Mirrors <c>RunAsEmulator</c>.
    /// </summary>
    /// <remarks>On Apple Silicon the SQL Server image crashes under QEMU; enable Docker Desktop's "Use Rosetta for x86_64/amd64 emulation".</remarks>
    public static IResourceBuilder<AzureServiceBusResource> RunAsEmulator(
        this IResourceBuilder<AzureServiceBusResource> builder,
        Action<IResourceBuilder<AzureServiceBusEmulatorResource>>? configureContainer = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // Tracked regardless of execution mode so the same AppHost fails the same way in run and publish mode.
        if (builder.Resource.EmulatorRequested)
            throw new InvalidOperationException("The Azure Service Bus resource is already configured to run as an emulator.");
        if (builder.Resource.RunAsExistingRequested)
            throw new InvalidOperationException("The Azure Service Bus resource is already configured with RunAsExisting.");
        builder.Resource.EmulatorRequested = true;
        if (builder.ApplicationBuilder.ExecutionContext.IsPublishMode) return builder;

        var app = builder.ApplicationBuilder;
        var emulator = new AzureServiceBusEmulatorResource(builder.Resource);
        var emulatorBuilder = app.AddResource(emulator)
            .WithImage(EmulatorImage, EmulatorTag)
            .WithImageRegistry("mcr.microsoft.com")
            .WithEndpoint(targetPort: 5672, name: AzureServiceBusEmulatorResource.EndpointName)
            .WithHttpEndpoint(targetPort: 5300, name: AzureServiceBusEmulatorResource.HealthEndpointName)
            .WithEnvironment(context =>
            {
                // Defaults only, so values set by the caller always win.
                var sql = emulator.SqlServerResource
                    ?? throw new InvalidOperationException("The SQL Server instance for the Azure Service Bus emulator was not configured. This indicates that 'RunAsEmulator' did not complete successfully.");
                context.EnvironmentVariables.TryAdd("ACCEPT_EULA", "Y");
                context.EnvironmentVariables.TryAdd("SQL_SERVER", sql.PrimaryEndpoint.Property(EndpointProperty.HostAndPort));
                context.EnvironmentVariables.TryAdd("MSSQL_SA_PASSWORD", sql.PasswordParameter);
            })
            .WithHttpHealthCheck("/health", endpointName: AzureServiceBusEmulatorResource.HealthEndpointName)
            .WithParentRelationship(builder)
            .ExcludeFromManifest();
        emulatorBuilder.WithEndpoint(AzureServiceBusEmulatorResource.HealthEndpointName, e => e.ExcludeReferenceEndpoint = true);

        // Declared entities may still change after RunAsEmulator(), so the config file is produced when the container is created.
        emulatorBuilder.WithContainerFiles(AzureServiceBusEmulatorResource.ConfigFilesPath, (_, _) =>
        {
            if (emulator.ConfigurationFilePath is { } path)
                return Task.FromResult<IEnumerable<ContainerFileSystemItem>>(
                    [new ContainerFile { Name = AzureServiceBusEmulatorResource.ConfigFile, SourcePath = path }]);

            var config = builder.Resource.ToEmulatorConfig();
            foreach (var configure in emulator.ConfigurationCallbacks) configure(config);
            return Task.FromResult<IEnumerable<ContainerFileSystemItem>>(
                [new ContainerFile { Name = AzureServiceBusEmulatorResource.ConfigFile, Contents = config.ToJsonString(new() { WriteIndented = true }) }]);
        });

        builder.Resource.Emulator = emulator;
        configureContainer?.Invoke(emulatorBuilder);

        // The SQL Server is resolved after the callback so WithSqlServer customizations take precedence over the defaults.
        if (emulator.ExistingSqlServer is { } existing)
        {
            emulator.SqlServer = existing.Resource;
            emulatorBuilder.WaitFor(existing);
        }
        else
        {
            var password = ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(
                app, $"{builder.Resource.Name}-sql-pwd", minLower: 1, minUpper: 1, minNumeric: 1);
            var sql = app.AddSqlServer($"{builder.Resource.Name}-mssql", app.CreateResourceBuilder(password))
                .WithParentRelationship(builder)
                .ExcludeFromManifest()
                // The SQL Server lives and dies with the emulator that owns it.
                .WithLifetimeOf(emulatorBuilder);
            foreach (var configureSql in emulator.SqlServerConfigurations) configureSql(sql);
            if (!sql.Resource.Annotations.OfType<EndpointAnnotation>().Any(e => e.Name == SqlServerEndpointName))
                throw new InvalidOperationException($"The SQL Server resource for the Azure Service Bus emulator must keep its '{SqlServerEndpointName}' endpoint. Update the 'WithSqlServer' callback so it does not remove or rename the endpoint.");
            emulator.SqlServer = sql.Resource;
            // The emulator fails to start when its backing store is not accepting connections yet.
            emulatorBuilder.WaitFor(sql);
        }

        ServiceBusStateInitializer.Register(app, builder.Resource);
        return builder;
    }

    /// <summary>
    /// Stores the emulator's state in the SQL Server container created for it (the default). Only states the intent
    /// explicitly; cannot be combined with <see cref="WithSqlServer(IResourceBuilder{AzureServiceBusEmulatorResource}, IResourceBuilder{SqlServerServerResource})"/>.
    /// </summary>
    public static IResourceBuilder<AzureServiceBusEmulatorResource> WithSqlServer(this IResourceBuilder<AzureServiceBusEmulatorResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithSqlServer(static _ => { });
    }

    /// <summary>
    /// Customizes the SQL Server container that backs the emulator (data volume, host port, password, container name...).
    /// Callable several times; callbacks apply in order. The <c>tcp</c> endpoint must not be removed or renamed.
    /// Cannot be combined with <see cref="WithSqlServer(IResourceBuilder{AzureServiceBusEmulatorResource}, IResourceBuilder{SqlServerServerResource})"/>.
    /// </summary>
    public static IResourceBuilder<AzureServiceBusEmulatorResource> WithSqlServer(
        this IResourceBuilder<AzureServiceBusEmulatorResource> builder, Action<IResourceBuilder<SqlServerServerResource>> configureSqlServer)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureSqlServer);
        if (builder.Resource.ExistingSqlServer is not null) throw new InvalidOperationException(SqlServerConflictMessage);
        builder.Resource.SqlServerConfigurations.Add(configureSqlServer);
        return builder;
    }

    /// <summary>
    /// Uses an existing SQL Server resource for the emulator's state instead of creating one. The emulator waits for it to be
    /// healthy and connects over the container network with its primary endpoint and password. The last call wins; cannot be
    /// combined with the callback overload.
    /// </summary>
    public static IResourceBuilder<AzureServiceBusEmulatorResource> WithSqlServer(
        this IResourceBuilder<AzureServiceBusEmulatorResource> builder, IResourceBuilder<SqlServerServerResource> sqlServer)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(sqlServer);
        if (builder.Resource.SqlServerConfigurations.Count > 0) throw new InvalidOperationException(SqlServerConflictMessage);
        builder.Resource.ExistingSqlServer = sqlServer;
        return builder;
    }

    /// <summary>Uses a hand-written emulator <c>Config.json</c> instead of one generated from the declared queues and topics.</summary>
    public static IResourceBuilder<AzureServiceBusEmulatorResource> WithConfigurationFile(
        this IResourceBuilder<AzureServiceBusEmulatorResource> builder, string path)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(path);
        builder.Resource.ConfigurationFilePath = path;
        return builder;
    }

    /// <summary>Alters the generated emulator <c>Config.json</c> document.</summary>
    public static IResourceBuilder<AzureServiceBusEmulatorResource> WithConfiguration(
        this IResourceBuilder<AzureServiceBusEmulatorResource> builder, Action<JsonNode> configJson)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configJson);
        builder.Resource.ConfigurationCallbacks.Add(configJson);
        return builder;
    }

    /// <summary>Sets the host port of the emulator's AMQP endpoint (a random port is used otherwise).</summary>
    public static IResourceBuilder<AzureServiceBusEmulatorResource> WithHostPort(
        this IResourceBuilder<AzureServiceBusEmulatorResource> builder, int? port)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithEndpoint(AzureServiceBusEmulatorResource.EndpointName, e => e.Port = port);
    }

    /// <summary>
    /// For an <c>AsExisting</c>/<c>PublishAsExisting</c> namespace: uses <paramref name="endpoint"/> (e.g.
    /// <c>mynamespace.servicebus.windows.net</c>, or just the namespace name, or <c>https://mynamespace.servicebus.windows.net:443/</c>) as the connection value
    /// when running locally, so no generated Pulumi project or deployed stack output is needed. No effect in publish mode.
    /// </summary>
    public static IResourceBuilder<AzureServiceBusResource> RunAsExisting(
        this IResourceBuilder<AzureServiceBusResource> builder, string endpoint)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        // A bare namespace name (as passed to the official RunAsExisting) expands to its host name.
        if (!endpoint.Contains('.') && !endpoint.Contains(':')) endpoint += ".servicebus.windows.net";
        return builder.RunAsExistingCore(ReferenceExpression.Create($"{endpoint}"));
    }

    /// <summary>Like <see cref="RunAsExisting(IResourceBuilder{AzureServiceBusResource}, string)"/> with the endpoint supplied by a parameter.</summary>
    public static IResourceBuilder<AzureServiceBusResource> RunAsExisting(
        this IResourceBuilder<AzureServiceBusResource> builder, IResourceBuilder<ParameterResource> endpoint)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(endpoint);
        return builder.RunAsExistingCore(ReferenceExpression.Create($"{endpoint.Resource}"));
    }

    private static IResourceBuilder<AzureServiceBusResource> RunAsExistingCore(
        this IResourceBuilder<AzureServiceBusResource> builder, ReferenceExpression endpoint)
    {
        if (builder.Resource.EmulatorRequested)
            throw new InvalidOperationException("The Azure Service Bus resource is already configured to run as an emulator.");
        builder.Resource.RunAsExistingRequested = true;
        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            builder.Resource.RunEndpoint = endpoint;
            ServiceBusStateInitializer.Register(builder.ApplicationBuilder, builder.Resource);
        }
        return builder;
    }
}
