using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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

    internal string? ConfigurationFilePath { get; set; }
    internal List<Action<JsonNode>> ConfigurationCallbacks { get; } = [];
}

/// <summary>Opens a TCP connection to a container endpoint; the emulator fails to start while its SQL Server is not accepting connections.</summary>
internal sealed class TcpEndpointHealthCheck(EndpointReference endpoint) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!endpoint.IsAllocated) return HealthCheckResult.Unhealthy("endpoint not allocated yet");
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or IOException)
        {
            return HealthCheckResult.Unhealthy(e.Message);
        }
    }
}

public static class AzureServiceBusEmulatorExtensions
{
    internal const string EmulatorImage = "azure-messaging/servicebus-emulator";
    internal const string EmulatorTag = "2.0.0";

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
        if (builder.Resource.Emulator is not null)
            throw new InvalidOperationException("The Azure Service Bus resource is already configured to run as an emulator.");
        if (builder.ApplicationBuilder.ExecutionContext.IsPublishMode) return builder;

        var app = builder.ApplicationBuilder;
        var password = ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(
            app, $"{builder.Resource.Name}-sql-pwd", minLower: 1, minUpper: 1, minNumeric: 1);

        var sql = app.AddContainer($"{builder.Resource.Name}-mssql", "mcr.microsoft.com/mssql/server", "2022-latest")
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_SA_PASSWORD", password)
            .WithEndpoint(targetPort: 1433, name: "tcp")
            .WithParentRelationship(builder)
            .ExcludeFromManifest();
        var sqlEndpoint = sql.GetEndpoint("tcp");
        var sqlHealthKey = $"{sql.Resource.Name}_tcp";
        app.Services.AddHealthChecks().Add(new HealthCheckRegistration(sqlHealthKey, _ => new TcpEndpointHealthCheck(sqlEndpoint), null, null));
        sql.WithAnnotation(new HealthCheckAnnotation(sqlHealthKey));

        var emulator = new AzureServiceBusEmulatorResource(builder.Resource) { SqlServer = sql.Resource };
        var emulatorBuilder = app.AddResource(emulator)
            .WithImage(EmulatorImage, EmulatorTag)
            .WithImageRegistry("mcr.microsoft.com")
            .WithEndpoint(targetPort: 5672, name: AzureServiceBusEmulatorResource.EndpointName)
            .WithHttpEndpoint(targetPort: 5300, name: AzureServiceBusEmulatorResource.HealthEndpointName)
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("SQL_SERVER", sqlEndpoint.Property(EndpointProperty.HostAndPort))
            .WithEnvironment("MSSQL_SA_PASSWORD", password)
            .WithHttpHealthCheck("/health", endpointName: AzureServiceBusEmulatorResource.HealthEndpointName)
            .WithParentRelationship(builder)
            .WaitFor(sql)
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
    /// <c>mynamespace.servicebus.windows.net</c> or <c>https://mynamespace.servicebus.windows.net:443/</c>) as the connection value
    /// when running locally, so no generated Pulumi project or deployed stack output is needed. No effect in publish mode.
    /// </summary>
    public static IResourceBuilder<AzureServiceBusResource> RunAsExisting(
        this IResourceBuilder<AzureServiceBusResource> builder, string endpoint)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
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
        if (builder.Resource.Emulator is not null)
            throw new InvalidOperationException("The Azure Service Bus resource is already configured to run as an emulator.");
        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode) builder.Resource.RunEndpoint = endpoint;
        return builder;
    }
}
