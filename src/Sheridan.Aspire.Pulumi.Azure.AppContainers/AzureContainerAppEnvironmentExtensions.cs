using Sheridan.Aspire.Pulumi;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure.AppContainers;
using Sheridan.Aspire.Pulumi.Azure.Base;
using Sheridan.Aspire.Pulumi.Azure.ContainerRegistry;
using Sheridan.Aspire.Pulumi.Azure.OperationalInsights;

namespace Aspire.Hosting;

/// <summary>
/// Pulumi counterparts of the extension methods in the official <c>Aspire.Hosting.Azure.AppContainers</c> package.
/// <c>WithLocation</c>, <c>WithPulumiStack</c> and <c>WithExistingResourceGroup</c> come from the Azure.Base package;
/// <c>WithDelegatedSubnet</c> (VNet integration) from the Azure.Network package.
/// </summary>
public static class AzureContainerAppEnvironmentExtensions
{
    /// <summary>Adds an Azure Container Apps environment that the Pulumi stack provisions. Mirrors <c>AddAzureContainerAppEnvironment</c>.</summary>
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> AddPulumiAzureContainerAppEnvironment(
        this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new AzureContainerAppEnvironmentResource(name)).ExcludeFromManifest().HideInRunMode();
    }

    /// <summary>Alias for <see cref="AddPulumiAzureContainerAppEnvironment"/>.</summary>
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> AddPulumiContainerAppEnvironment(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiAzureContainerAppEnvironment(name);

    /// <summary>Alias for <see cref="AddPulumiAzureContainerAppEnvironment"/> named after the package (<c>AddPulumiAppContainers</c>).</summary>
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> AddPulumiAppContainers(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiAzureContainerAppEnvironment(name);

    /// <summary>
    /// Deploys into an existing Container Apps environment instead of creating one: the managed environment is looked up by
    /// name and resource group (nothing is created for it, nor its Log Analytics workspace or dashboard), while the container
    /// apps and jobs, the ACR pull identity and its AcrPull assignment are still created. Because the environment has no registry of
    /// its own here, pair this with <c>WithAzureContainerRegistry</c>. Mirrors <c>PublishAsExisting</c>.
    /// </summary>
    /// <param name="existingResourceName">Name of the existing managed environment.</param>
    /// <param name="resourceGroupName">Resource group of the environment; when omitted, the resource group configured for this resource is used. The container apps are created in the resource group configured for this resource, not necessarily the environment's.</param>
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> PublishAsExisting(
        this IResourceBuilder<AzureContainerAppEnvironmentResource> builder, string existingResourceName, string? resourceGroupName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(existingResourceName);
        if (resourceGroupName is not null) ArgumentException.ThrowIfNullOrEmpty(resourceGroupName);
        builder.Resource.ExistingEnvironmentName = existingResourceName;
        builder.Resource.ExistingEnvironmentResourceGroup = resourceGroupName;
        return builder;
    }

    /// <summary>Alias of <see cref="PublishAsExisting"/> (this resource has no local run mode). Mirrors <c>AsExisting</c>.</summary>
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> AsExisting(
        this IResourceBuilder<AzureContainerAppEnvironmentResource> builder, string existingResourceName, string? resourceGroupName = null) =>
        builder.PublishAsExisting(existingResourceName, resourceGroupName);

    /// <summary>Uses an existing Pulumi container registry instead of provisioning one for the environment. Mirrors <c>WithAzureContainerRegistry</c>.</summary>
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> WithAzureContainerRegistry(
        this IResourceBuilder<AzureContainerAppEnvironmentResource> builder, IResourceBuilder<AzureContainerRegistryResource> registry)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(registry);
        builder.Resource.Registry = registry.Resource;
        return builder;
    }

    /// <summary>Uses an existing Pulumi Log Analytics workspace instead of provisioning one. Mirrors <c>WithAzureLogAnalyticsWorkspace</c>.</summary>
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> WithAzureLogAnalyticsWorkspace(
        this IResourceBuilder<AzureContainerAppEnvironmentResource> builder, IResourceBuilder<LogAnalyticsWorkspaceResource> workspace)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(workspace);
        builder.Resource.LogAnalyticsWorkspace = workspace.Resource;
        return builder;
    }

    /// <summary>Alias for <see cref="WithAzureLogAnalyticsWorkspace"/>.</summary>
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> WithLogAnalyticsWorkspace(
        this IResourceBuilder<AzureContainerAppEnvironmentResource> builder, IResourceBuilder<LogAnalyticsWorkspaceResource> workspace) =>
        builder.WithAzureLogAnalyticsWorkspace(workspace);

    /// <summary>Uses a Pulumi user-assigned identity (granted AcrPull on the registry) for image pulls. Mirrors <c>WithAcrPullIdentity</c>.</summary>
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> WithAcrPullIdentity(
        this IResourceBuilder<AzureContainerAppEnvironmentResource> builder, IResourceBuilder<PulumiAzureUserAssignedIdentityResource> identity)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(identity);
        builder.Resource.PullIdentity = identity.Resource;
        return builder;
    }

    /// <summary>
    /// Builds images into the local container runtime and tags them with the registry reference (<c>&lt;loginServer&gt;/&lt;app&gt;:&lt;tag&gt;</c>)
    /// without pushing or running <c>az acr login</c>. For emulators that run replicas through the host Docker daemon (for example floci-az).
    /// </summary>
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> WithLocalImages(
        this IResourceBuilder<AzureContainerAppEnvironmentResource> builder, bool enable = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Resource.LocalImages = enable;
        return builder;
    }

    /// <summary>Uses a stable, caller-supplied image tag instead of the per-deploy timestamp. An unchanged tag does not create a new revision.</summary>
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> WithImageTag(
        this IResourceBuilder<AzureContainerAppEnvironmentResource> builder, string tag)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        if (!System.Text.RegularExpressions.Regex.IsMatch(tag, "^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$"))
            throw new ArgumentException($"'{tag}' is not a valid container image tag.", nameof(tag));
        builder.Resource.ImageTag = tag;
        return builder;
    }

    /// <summary>Enables or disables the Aspire dashboard component on the environment. Mirrors <c>WithDashboard</c> (default enabled).</summary>
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> WithDashboard(
        this IResourceBuilder<AzureContainerAppEnvironmentResource> builder, bool enable = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Resource.DashboardEnabled = enable;
        builder.Resource.DashboardConfigured = true;
        return builder;
    }

    /// <summary>
    /// Omits the AcrPull role assignment, the container apps' user-assigned identity and their identity-based registry auth
    /// (and the <c>AZURE_CLIENT_ID</c> variable). For Azure emulators that don't implement <c>Microsoft.Authorization</c> role assignments.
    /// </summary>
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> WithoutAcrPullRoleAssignment(
        this IResourceBuilder<AzureContainerAppEnvironmentResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Resource.AcrPullRoleAssignmentEnabled = false;
        return builder;
    }

    /// <summary>Deploys the resource as a manually-triggered Container Apps job. Mirrors <c>PublishAsAzureContainerAppJob</c>.</summary>
    public static IResourceBuilder<T> PublishAsPulumiContainerAppJob<T>(this IResourceBuilder<T> builder) where T : IComputeResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithAnnotation(new PulumiContainerAppJobAnnotation(null), ResourceAnnotationMutationBehavior.Replace);
    }

    /// <summary>Deploys the resource as a scheduled Container Apps job. Mirrors <c>PublishAsScheduledAzureContainerAppJob</c>.</summary>
    public static IResourceBuilder<T> PublishAsScheduledPulumiContainerAppJob<T>(this IResourceBuilder<T> builder, string cronExpression)
        where T : IComputeResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);
        return builder.WithAnnotation(new PulumiContainerAppJobAnnotation(cronExpression), ResourceAnnotationMutationBehavior.Replace);
    }

    /// <summary>
    /// Customizes the scale settings (replicas, cooldown, scale rules) of the container app emitted for the compute resource. Pulumi counterpart of
    /// <c>PublishAsAzureContainerApp((infra, app) =&gt; app.Template.Scale...)</c>. Ignored for resources published as jobs.
    /// </summary>
    public static IResourceBuilder<T> PublishAsPulumiContainerApp<T>(this IResourceBuilder<T> builder, Action<PulumiContainerAppScale> configureScale)
        where T : IComputeResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureScale);
        var scale = builder.Resource.Annotations.OfType<PulumiContainerAppScaleAnnotation>().LastOrDefault()?.Scale;
        if (scale is null)
        {
            scale = new PulumiContainerAppScale();
            builder.WithAnnotation(new PulumiContainerAppScaleAnnotation(scale), ResourceAnnotationMutationBehavior.Replace);
        }
        configureScale(scale);
        return builder;
    }

    /// <summary>Sets the container app's minimum and maximum replica counts and optional cooldown period (seconds).</summary>
    public static IResourceBuilder<T> WithPulumiContainerAppScale<T>(this IResourceBuilder<T> builder, int minReplicas, int maxReplicas, int? cooldownPeriod = null)
        where T : IComputeResource =>
        builder.PublishAsPulumiContainerApp(s => { s.MinReplicas = minReplicas; s.MaxReplicas = maxReplicas; if (cooldownPeriod is not null) s.CooldownPeriod = cooldownPeriod; });

    /// <summary>Adds an HTTP concurrency scale rule to the container app.</summary>
    public static IResourceBuilder<T> WithPulumiHttpScaleRule<T>(this IResourceBuilder<T> builder, string name, int concurrentRequests)
        where T : IComputeResource =>
        builder.PublishAsPulumiContainerApp(s => s.AddHttpRule(name, concurrentRequests));

    /// <summary>Adds a CPU utilization (percent) scale rule to the container app.</summary>
    public static IResourceBuilder<T> WithPulumiCpuScaleRule<T>(this IResourceBuilder<T> builder, string name, int utilizationPercentage)
        where T : IComputeResource =>
        builder.PublishAsPulumiContainerApp(s => s.AddCpuRule(name, utilizationPercentage));

    /// <summary>Adds a memory utilization (percent) scale rule to the container app.</summary>
    public static IResourceBuilder<T> WithPulumiMemoryScaleRule<T>(this IResourceBuilder<T> builder, string name, int utilizationPercentage)
        where T : IComputeResource =>
        builder.PublishAsPulumiContainerApp(s => s.AddMemoryRule(name, utilizationPercentage));

    /// <summary>Adds a custom KEDA scale rule (e.g. queue length) to the container app, optionally authenticating with the environment's shared identity.</summary>
    public static IResourceBuilder<T> WithPulumiCustomScaleRule<T>(this IResourceBuilder<T> builder, string name, string type,
        IReadOnlyDictionary<string, string> metadata, bool useEnvironmentIdentity = false)
        where T : IComputeResource =>
        builder.PublishAsPulumiContainerApp(s => s.AddCustomRule(name, type, metadata, useEnvironmentIdentity));
}
