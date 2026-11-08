using System.Text;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure.ContainerRegistry;

namespace Aspire.Hosting;

/// <summary>Pulumi counterparts of the extension methods in the official <c>Aspire.Hosting.Azure.ContainerRegistry</c> package.</summary>
public static class AzureContainerRegistryExtensions
{
    /// <summary>Adds an Azure Container Registry provisioned by Pulumi. Mirrors <c>AddAzureContainerRegistry</c>.</summary>
    public static IResourceBuilder<AzureContainerRegistryResource> AddPulumiAzureContainerRegistry(
        this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new AzureContainerRegistryResource(name)).ExcludeFromManifest();
    }

    /// <summary>Alias for <see cref="AddPulumiAzureContainerRegistry"/>.</summary>
    public static IResourceBuilder<AzureContainerRegistryResource> AddPulumiContainerRegistry(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiAzureContainerRegistry(name);

    /// <summary>
    /// Uses an existing registry instead of creating one: it is looked up by name and resource group (login server and id
    /// are exposed as usual) and role assignments are still applied to it. Mirrors <c>PublishAsExisting</c>.
    /// </summary>
    /// <param name="existingResourceName">Name of the existing registry.</param>
    /// <param name="resourceGroupName">Resource group of the registry; when omitted, the resource group configured for this resource (<c>WithResourceGroup</c>/<c>WithExistingResourceGroup</c>) is used.</param>
    public static IResourceBuilder<AzureContainerRegistryResource> PublishAsExisting(
        this IResourceBuilder<AzureContainerRegistryResource> builder, string existingResourceName, string? resourceGroupName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(existingResourceName);
        if (resourceGroupName is not null) ArgumentException.ThrowIfNullOrEmpty(resourceGroupName);
        builder.Resource.ExistingRegistryName = existingResourceName;
        if (resourceGroupName is not null) builder.Resource.ExistingResourceGroup = resourceGroupName;
        return builder;
    }

    /// <summary>Alias of <see cref="PublishAsExisting"/> (this resource has no local run mode). Mirrors <c>AsExisting</c>.</summary>
    public static IResourceBuilder<AzureContainerRegistryResource> AsExisting(
        this IResourceBuilder<AzureContainerRegistryResource> builder, string existingResourceName, string? resourceGroupName = null) =>
        builder.PublishAsExisting(existingResourceName, resourceGroupName);

    /// <summary>
    /// Adds a scheduled <c>acr purge</c> task. Mirrors <c>WithPurgeTask</c>: <paramref name="schedule"/> is a five-part cron
    /// expression, <paramref name="filter"/> defaults to <c>.*:.*</c>, <paramref name="ago"/> to <c>0d</c>, <paramref name="keep"/> to 3.
    /// </summary>
    public static IResourceBuilder<AzureContainerRegistryResource> WithPurgeTask(
        this IResourceBuilder<AzureContainerRegistryResource> builder, string schedule, string? filter = null,
        TimeSpan? ago = null, int keep = 3)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedule);
        if (schedule.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != 5)
            throw new ArgumentException("The schedule must be a five-part cron expression (minute hour day-of-month month day-of-week).", nameof(schedule));
        if (keep <= 0) throw new ArgumentOutOfRangeException(nameof(keep), "keep must be greater than zero.");
        if (filter is not null && filter.Contains('\'')) throw new ArgumentException("The filter must not contain single quotes.", nameof(filter));

        builder.Resource.PurgeTasks.Add(new AzureContainerRegistryPurgeTask(schedule, filter ?? ".*:.*", FormatAgo(ago ?? TimeSpan.Zero), keep));
        return builder;
    }

    /// <summary>
    /// Assigns built-in roles on the registry to the Pulumi-managed identity of <paramref name="builder"/>'s resource
    /// (a Pulumi user-assigned identity or a Pulumi compute environment). Mirrors <c>WithRoleAssignments</c>.
    /// </summary>
    public static IResourceBuilder<T> WithRoleAssignments<T>(
        this IResourceBuilder<T> builder, IResourceBuilder<AzureContainerRegistryResource> target,
        params AzureContainerRegistryRole[] roles) where T : IResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(roles);
        if (roles.Length == 0) throw new ArgumentException("At least one role is required.", nameof(roles));
        return builder.WithAnnotation(new AzureContainerRegistryRoleAssignmentAnnotation(target.Resource, roles));
    }

    /// <summary>Formats a TimeSpan as a Go-style duration (e.g. <c>2d3h6m</c>) for <c>acr purge --ago</c>.</summary>
    internal static string FormatAgo(TimeSpan ago)
    {
        if (ago < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ago));
        var sb = new StringBuilder();
        if (ago.Days > 0) sb.Append(ago.Days).Append('d');
        if (ago.Hours > 0) sb.Append(ago.Hours).Append('h');
        if (ago.Minutes > 0) sb.Append(ago.Minutes).Append('m');
        return sb.Length == 0 ? "0d" : sb.ToString();
    }
}
