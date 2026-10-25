using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Azure.AppContainers;
using AspirePulumiPackages.Pulumi.Azure.Base;
using AspirePulumiPackages.Pulumi.Azure.ContainerRegistry;
using AspirePulumiPackages.Pulumi.Azure.OperationalInsights;

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
        return builder.AddResource(new AzureContainerAppEnvironmentResource(name)).ExcludeFromManifest();
    }

    /// <summary>Alias for <see cref="AddPulumiAzureContainerAppEnvironment"/>.</summary>
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> AddPulumiContainerAppEnvironment(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiAzureContainerAppEnvironment(name);

    /// <summary>Alias for <see cref="AddPulumiAzureContainerAppEnvironment"/> named after the package (<c>AddPulumiAppContainers</c>).</summary>
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> AddPulumiAppContainers(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiAzureContainerAppEnvironment(name);

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

    /// <summary>Enables or disables the Aspire dashboard component on the environment. Mirrors <c>WithDashboard</c> (default enabled).</summary>
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> WithDashboard(
        this IResourceBuilder<AzureContainerAppEnvironmentResource> builder, bool enable = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Resource.DashboardEnabled = enable;
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
}
