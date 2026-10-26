using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Azure.Base;
using AspirePulumiPackages.Pulumi.Azure.ContainerRegistry;
using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.Sandboxes;

/// <summary>Pulumi equivalents of the Aspire.Hosting.Azure.Sandboxes extension methods.</summary>
public static class AzureSandboxesExtensions
{
    /// <summary>Adds an Azure Container Apps sandbox group provisioned by the Pulumi stack (the equivalent of AddAzureSandboxGroup).</summary>
    public static IResourceBuilder<AzureSandboxGroupResource> AddPulumiSandboxGroup(this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return builder.AddResource(new AzureSandboxGroupResource(name)).ExcludeFromManifest();
    }

    /// <summary>Alias of <see cref="AddPulumiSandboxGroup"/> following the <c>AddPulumiAzureX</c> naming used by the other Azure integrations.</summary>
    public static IResourceBuilder<AzureSandboxGroupResource> AddPulumiAzureSandboxGroup(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiSandboxGroup(name);

    /// <summary>Alias of <see cref="AddPulumiSandboxGroup"/>, matching the package name.</summary>
    public static IResourceBuilder<AzureSandboxGroupResource> AddPulumiSandboxes(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiSandboxGroup(name);

    /// <summary>Clears the managed identities configured for sandbox workloads (the image-pull identity is always attached).</summary>
    public static IResourceBuilder<AzureSandboxGroupResource> WithNoManagedIdentity(this IResourceBuilder<AzureSandboxGroupResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Resource.SystemAssigned = false;
        builder.Resource.WorkloadIdentities.Clear();
        return builder;
    }

    /// <summary>Configures a system-assigned managed identity for sandbox workloads (clears user-assigned workload identities, as upstream).</summary>
    public static IResourceBuilder<AzureSandboxGroupResource> WithSystemAssignedIdentity(this IResourceBuilder<AzureSandboxGroupResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Resource.SystemAssigned = true;
        builder.Resource.WorkloadIdentities.Clear();
        return builder;
    }

    /// <summary>Adds a user-assigned managed identity for sandbox workloads.</summary>
    public static IResourceBuilder<AzureSandboxGroupResource> WithUserAssignedIdentity(
        this IResourceBuilder<AzureSandboxGroupResource> builder, IResourceBuilder<PulumiAzureUserAssignedIdentityResource> identity)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(identity);
        if (!builder.Resource.WorkloadIdentities.Contains(identity.Resource))
            builder.Resource.WorkloadIdentities.Add(identity.Resource);
        return builder;
    }

    /// <summary>
    /// Uses a Pulumi user-assigned identity for image pulls instead of the group's own. It is attached to the group, but
    /// (as with the official <c>WithAcrPullIdentity</c>) the caller remains responsible for granting it <c>AcrPull</c>
    /// on the registry (see <c>WithRoleAssignments</c> in the ContainerRegistry package).
    /// </summary>
    public static IResourceBuilder<AzureSandboxGroupResource> WithAcrPullIdentity(
        this IResourceBuilder<AzureSandboxGroupResource> builder, IResourceBuilder<PulumiAzureUserAssignedIdentityResource> identity)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(identity);
        builder.Resource.PullIdentity = identity.Resource;
        return builder;
    }

    /// <summary>Uses a Pulumi container registry instead of the group's own <c>{name}-acr</c>. Mirrors <c>WithAzureContainerRegistry</c>.</summary>
    public static IResourceBuilder<AzureSandboxGroupResource> WithAzureContainerRegistry(
        this IResourceBuilder<AzureSandboxGroupResource> builder, IResourceBuilder<AzureContainerRegistryResource> registry)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(registry);
        builder.Resource.Registry = registry.Resource;
        return builder;
    }

    /// <summary>
    /// Uses an existing sandbox group instead of creating one (nothing is provisioned for the group itself and no deployer role is
    /// assigned: grant the deploying principal <c>Container Apps SandboxGroup Data Owner</c> yourself). As upstream, an existing group
    /// requires <c>WithAcrPullIdentity</c> with an identity already attached to it that has AcrPull on the registry, and does not
    /// accept workload identities. Mirrors <c>PublishAsExisting</c>.
    /// </summary>
    public static IResourceBuilder<AzureSandboxGroupResource> PublishAsExisting(
        this IResourceBuilder<AzureSandboxGroupResource> builder, string existingResourceName, string? resourceGroupName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(existingResourceName);
        ArgumentException.ThrowIfNullOrEmpty(resourceGroupName);
        builder.Resource.ExistingGroupName = existingResourceName;
        builder.Resource.ExistingResourceGroup = resourceGroupName;
        return builder;
    }

    /// <summary>Alias of <see cref="PublishAsExisting"/> (this resource has no local run mode). Mirrors <c>AsExisting</c>.</summary>
    public static IResourceBuilder<AzureSandboxGroupResource> AsExisting(
        this IResourceBuilder<AzureSandboxGroupResource> builder, string existingResourceName, string? resourceGroupName) =>
        builder.PublishAsExisting(existingResourceName, resourceGroupName);

    /// <summary>
    /// Configures the sandbox runtime options (tier, auto-suspend, auto-delete, endpoint access) used when the compute resource is
    /// published to a sandbox group. Mirrors <c>PublishAsAzureSandbox</c>; a no-op outside publish mode.
    /// </summary>
    public static IResourceBuilder<T> PublishAsAzureSandbox<T>(this IResourceBuilder<T> builder, AzureSandboxOptions? options = null)
        where T : IComputeResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!builder.ApplicationBuilder.ExecutionContext.IsPublishMode) return builder;

        var effective = options ?? new AzureSandboxOptions();
        ValidateSandboxOptions(effective);
        var copy = new AzureSandboxOptions
        {
            Tier = effective.Tier,
            AutoSuspendEnabled = effective.AutoSuspendEnabled,
            AutoSuspendInterval = effective.AutoSuspendInterval,
            AutoSuspendMode = effective.AutoSuspendMode,
            AutoDeleteEnabled = effective.AutoDeleteEnabled,
            AutoDeleteInterval = effective.AutoDeleteInterval,
            AutoDeleteTrigger = effective.AutoDeleteTrigger,
            Endpoints = effective.Endpoints?.Select(e => new AzureSandboxEndpointOptions { Name = e.Name, Anonymous = e.Anonymous }).ToArray(),
        };
        return builder.WithAnnotation(new AzureSandboxContainerOptionsAnnotation(copy), ResourceAnnotationMutationBehavior.Replace);
    }

    /// <summary>Configures the sandbox runtime options with a callback. Mirrors the <c>Action&lt;AzureSandboxOptions&gt;</c> overload of <c>PublishAsAzureSandbox</c>.</summary>
    public static IResourceBuilder<T> PublishAsAzureSandbox<T>(this IResourceBuilder<T> builder, Action<AzureSandboxOptions> configure)
        where T : IComputeResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        if (!builder.ApplicationBuilder.ExecutionContext.IsPublishMode) return builder;
        var options = new AzureSandboxOptions();
        configure(options);
        return builder.PublishAsAzureSandbox(options);
    }

    /// <summary>
    /// Runs the compute resource's sandbox as a Pulumi user-assigned identity (<c>AZURE_CLIENT_ID</c> is set for it). The identity is
    /// attached to the sandbox group automatically. Counterpart of <c>WithAzureUserAssignedIdentity</c> on a sandbox-hosted resource.
    /// </summary>
    public static IResourceBuilder<T> WithSandboxIdentity<T>(this IResourceBuilder<T> builder, IResourceBuilder<PulumiAzureUserAssignedIdentityResource> identity)
        where T : IComputeResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(identity);
        return builder.WithAnnotation(new AzureSandboxIdentityAnnotation(identity.Resource), ResourceAnnotationMutationBehavior.Replace);
    }

    private static void ValidateSandboxOptions(AzureSandboxOptions options)
    {
        if (!Enum.IsDefined(options.Tier))
            throw new ArgumentException($"'{options.Tier}' is not a valid Azure sandbox tier.", nameof(options));
        ValidateDuration(options.AutoSuspendInterval, "AutoSuspendInterval", TimeSpan.FromSeconds(int.MaxValue));
        ValidateDuration(options.AutoDeleteInterval, "AutoDeleteInterval", null);
        if (options.AutoSuspendMode is { } mode && !Enum.IsDefined(mode))
            throw new ArgumentException($"'{mode}' is not a valid AutoSuspendMode value.", nameof(options));
        if (options.AutoDeleteTrigger is { } trigger && !Enum.IsDefined(trigger))
            throw new ArgumentException($"'{trigger}' is not a valid AutoDeleteTrigger value.", nameof(options));
        if (!options.AutoSuspendEnabled.HasValue && (options.AutoSuspendInterval.HasValue || options.AutoSuspendMode.HasValue))
            throw new ArgumentException("AutoSuspendEnabled must be set when configuring auto-suspend interval or mode.", nameof(options));
        if (!options.AutoDeleteEnabled.HasValue && (options.AutoDeleteInterval.HasValue || options.AutoDeleteTrigger.HasValue))
            throw new ArgumentException("AutoDeleteEnabled must be set when configuring auto-delete interval or trigger.", nameof(options));

        if (options.Endpoints is null) return;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in options.Endpoints)
        {
            if (endpoint is null) throw new ArgumentException("Endpoint options cannot contain null values.", nameof(options));
            if (string.IsNullOrWhiteSpace(endpoint.Name)) throw new ArgumentException("Endpoint option names cannot be empty.", nameof(options));
            if (!names.Add(endpoint.Name)) throw new ArgumentException($"Endpoint option '{endpoint.Name}' is configured more than once.", nameof(options));
        }
    }

    private static void ValidateDuration(TimeSpan? value, string name, TimeSpan? maximum)
    {
        if (value is not { } v) return;
        if (v < TimeSpan.Zero) throw new ArgumentOutOfRangeException(name, "The value cannot be negative.");
        if (v.Ticks % TimeSpan.TicksPerSecond != 0) throw new ArgumentException("The value must use whole-second precision.", name);
        if (maximum is { } max && v > max) throw new ArgumentOutOfRangeException(name, $"The value cannot exceed {max}.");
    }

    /// <summary>Pulumi equivalent of <c>ConfigureInfrastructure</c>: customize the sandbox group resources before the program is emitted.</summary>
    public static IResourceBuilder<AzureSandboxGroupResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureSandboxGroupResource> builder, Action<PulumiSandboxesInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiSandboxesInfrastructure(infra, owner)));
    }
}
