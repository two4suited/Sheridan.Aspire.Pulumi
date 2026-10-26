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

    /// <summary>Pulumi equivalent of <c>ConfigureInfrastructure</c>: customize the sandbox group resources before the program is emitted.</summary>
    public static IResourceBuilder<AzureSandboxGroupResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureSandboxGroupResource> builder, Action<PulumiSandboxesInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiSandboxesInfrastructure(infra, owner)));
    }
}
