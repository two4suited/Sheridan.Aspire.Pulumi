using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace AspirePulumiPackages.Pulumi.Azure.AppConfiguration;

/// <summary>Pulumi equivalents of the Aspire.Hosting.Azure.AppConfiguration extension methods.</summary>
public static class AzureAppConfigurationExtensions
{
    /// <summary>Adds an Azure App Configuration store provisioned by the Pulumi stack (the equivalent of AddAzureAppConfiguration).</summary>
    public static IResourceBuilder<AzureAppConfigurationResource> AddPulumiAppConfiguration(this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new AzureAppConfigurationResource(name)).ExcludeFromManifest();
    }

    /// <summary>Alias of <see cref="AddPulumiAppConfiguration"/> following the <c>AddPulumiAzureX</c> naming used by the other Azure integrations.</summary>
    public static IResourceBuilder<AzureAppConfigurationResource> AddPulumiAzureAppConfiguration(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiAppConfiguration(name);

    /// <summary>
    /// Uses an existing store instead of creating one (nothing is provisioned for the store itself, but role assignments
    /// are still applied to it). Mirrors <c>PublishAsExisting</c>.
    /// </summary>
    public static IResourceBuilder<AzureAppConfigurationResource> PublishAsExisting(
        this IResourceBuilder<AzureAppConfigurationResource> builder, string existingResourceName, string? resourceGroupName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(existingResourceName);
        ArgumentException.ThrowIfNullOrEmpty(resourceGroupName);
        builder.Resource.ExistingStoreName = existingResourceName;
        builder.Resource.ExistingResourceGroup = resourceGroupName;
        return builder;
    }

    /// <summary>Alias of <see cref="PublishAsExisting"/>. Mirrors <c>AsExisting</c>.</summary>
    public static IResourceBuilder<AzureAppConfigurationResource> AsExisting(
        this IResourceBuilder<AzureAppConfigurationResource> builder, string existingResourceName, string? resourceGroupName) =>
        builder.PublishAsExisting(existingResourceName, resourceGroupName);

    /// <summary>
    /// Runs the App Configuration emulator locally instead of connecting to the Pulumi-provisioned store (anonymous Owner access).
    /// No effect in publish mode. Mirrors <c>RunAsEmulator</c>.
    /// </summary>
    public static IResourceBuilder<AzureAppConfigurationResource> RunAsEmulator(
        this IResourceBuilder<AzureAppConfigurationResource> builder,
        Action<IResourceBuilder<AzureAppConfigurationEmulatorResource>>? configureContainer = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.ApplicationBuilder.ExecutionContext.IsPublishMode) return builder;

        var emulator = new AzureAppConfigurationEmulatorResource(builder.Resource);
        var emulatorBuilder = builder.ApplicationBuilder.AddResource(emulator)
            .WithImage("azure-app-configuration/app-configuration-emulator", "1.2.0")
            .WithImageRegistry("mcr.microsoft.com")
            .WithHttpEndpoint(targetPort: 8483, name: AzureAppConfigurationEmulatorResource.EndpointName)
            .WithHttpHealthCheck("/health", endpointName: AzureAppConfigurationEmulatorResource.EndpointName)
            .WithParentRelationship(builder)
            .ExcludeFromManifest()
            .WithAnonymousAccess();

        builder.Resource.Emulator = emulator;
        configureContainer?.Invoke(emulatorBuilder);
        return builder;
    }

    /// <summary>Adds a named volume for the emulator data (mounted at <c>/app/.aace</c>).</summary>
    public static IResourceBuilder<AzureAppConfigurationEmulatorResource> WithDataVolume(
        this IResourceBuilder<AzureAppConfigurationEmulatorResource> builder, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithVolume(name ?? $"{builder.Resource.InnerResource.Name}-data", "/app/.aace");
    }

    /// <summary>Mounts a host directory for the emulator data (at <c>/app/.aace</c>).</summary>
    public static IResourceBuilder<AzureAppConfigurationEmulatorResource> WithDataBindMount(
        this IResourceBuilder<AzureAppConfigurationEmulatorResource> builder, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithBindMount(path ?? $".aace/{builder.Resource.InnerResource.Name}", "/app/.aace");
    }

    /// <summary>Sets the host port of the emulator endpoint.</summary>
    public static IResourceBuilder<AzureAppConfigurationEmulatorResource> WithHostPort(
        this IResourceBuilder<AzureAppConfigurationEmulatorResource> builder, int? port)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithEndpoint(AzureAppConfigurationEmulatorResource.EndpointName, e => e.Port = port);
    }

    /// <summary>Enables anonymous access to the emulator with the given role (default <c>Owner</c>).</summary>
    public static IResourceBuilder<AzureAppConfigurationEmulatorResource> WithAnonymousAccess(
        this IResourceBuilder<AzureAppConfigurationEmulatorResource> builder, string role = "Owner")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(role);
        return builder
            .WithEnvironment("Tenant__AnonymousAuthEnabled", "true")
            .WithEnvironment("Authentication__Anonymous__AnonymousUserRole", role);
    }

    /// <summary>
    /// Replaces the default role assignment (App Configuration Data Owner) for the compute environment identity hosting
    /// <paramref name="builder"/> with the given roles on the store. Mirrors <c>WithRoleAssignments</c>.
    /// </summary>
    public static IResourceBuilder<T> WithRoleAssignments<T>(
        this IResourceBuilder<T> builder, IResourceBuilder<AzureAppConfigurationResource> target, params AzureAppConfigurationRole[] roles)
        where T : IResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(roles);
        foreach (var role in roles)
            if (!Enum.IsDefined(role)) throw new ArgumentException($"'{role}' is not a valid App Configuration role.", nameof(roles));
        return builder.WithAnnotation(new AzureAppConfigurationRoleAnnotation(target.Resource, roles));
    }
}
