using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure.AppContainers;
using Sheridan.Aspire.Pulumi.Azure.AppService;
using Sheridan.Aspire.Pulumi.Azure.Base;
using Sheridan.Aspire.Pulumi.Provisioning;

namespace Sheridan.Aspire.Pulumi.Azure.FrontDoor;

/// <summary>Pulumi equivalents of the Aspire.Hosting.Azure.FrontDoor extension methods.</summary>
public static class AzureFrontDoorExtensions
{
    /// <summary>Adds an Azure Front Door (Standard) profile provisioned by the Pulumi stack (the equivalent of AddAzureFrontDoor).</summary>
    public static IResourceBuilder<AzureFrontDoorResource> AddPulumiFrontDoor(this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new AzureFrontDoorResource(name)).ExcludeFromManifest();
    }

    /// <summary>Alias of <see cref="AddPulumiFrontDoor"/> following the <c>AddPulumiAzureX</c> naming used by the other Azure integrations.</summary>
    public static IResourceBuilder<AzureFrontDoorResource> AddPulumiAzureFrontDoor(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiFrontDoor(name);

    /// <summary>
    /// Adds an origin (backend). Each origin gets its own Front Door endpoint (<c>*.azurefd.net</c>), origin group and a
    /// default <c>/*</c> route, exactly like the official integration.
    /// </summary>
    public static IResourceBuilder<AzureFrontDoorResource> WithOrigin<T>(
        this IResourceBuilder<AzureFrontDoorResource> builder, IResourceBuilder<T> resource)
        where T : IComputeResource, IResourceWithEndpoints
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(resource);
        if (builder.Resource.Origins.Any(o => o.Name == resource.Resource.Name))
            throw new InvalidOperationException(
                $"Origin resource '{resource.Resource.Name}' has already been added to Azure Front Door resource '{builder.Resource.Name}'. Each origin can only be added once.");
        builder.Resource.Origins.Add(resource.Resource);
        return builder;
    }

    /// <summary>Pulumi equivalent of <c>ConfigureInfrastructure</c>: customize the Front Door resources before the program is emitted.</summary>
    public static IResourceBuilder<AzureFrontDoorResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureFrontDoorResource> builder, Action<PulumiFrontDoorInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiFrontDoorInfrastructure(infra, owner)));
    }

    /// <summary>
    /// Connects the Front Door to a Container Apps environment over Private Link (the equivalent of
    /// <c>WithAzureFrontDoorPrivateLink</c>). Public network access on the environment is disabled, the Front Door profile
    /// becomes Premium, and every origin hosted in the environment gets a shared Private Link resource targeting it
    /// (group <c>managedEnvironments</c>). The connection is created <b>Pending</b>: approve it on the environment in the portal
    /// or with <c>az network private-endpoint-connection approve</c>. Use <c>ConfigureInfrastructure</c> to override the
    /// origin's <c>sharedPrivateLinkResource.privateLinkLocation</c> if Front Door Private Link isn't available in the environment's region.
    /// </summary>
    public static IResourceBuilder<AzureContainerAppEnvironmentResource> WithAzureFrontDoorPrivateLink(
        this IResourceBuilder<AzureContainerAppEnvironmentResource> builder, IResourceBuilder<AzureFrontDoorResource> frontDoor)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(frontDoor);
        if (!builder.Resource.Annotations.OfType<PulumiPrivateLinkTargetAnnotation>().Any(a => a.Source == frontDoor.Resource))
            builder.Resource.Annotations.Add(new PulumiPrivateLinkTargetAnnotation(frontDoor.Resource));
        return builder;
    }

    /// <summary>
    /// Connects the Front Door to an App Service environment over Private Link (the equivalent of
    /// <c>WithAzureFrontDoorPrivateLink</c>). Public network access is disabled on every workload website in the environment
    /// (the dashboard site is unchanged), the Front Door profile becomes Premium, and each origin gets a shared Private Link
    /// resource targeting <b>its own website</b> (group <c>sites</c>, in the website's region). The connection is created
    /// <b>Pending</b>: approve it on each website in the portal or with <c>az network private-endpoint-connection approve</c>.
    /// The Front Door and the environment must be in the same Pulumi stack.
    /// </summary>
    public static IResourceBuilder<AzureAppServiceEnvironmentResource> WithAzureFrontDoorPrivateLink(
        this IResourceBuilder<AzureAppServiceEnvironmentResource> builder, IResourceBuilder<AzureFrontDoorResource> frontDoor)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(frontDoor);
        if (!builder.Resource.Annotations.OfType<PulumiPrivateLinkTargetAnnotation>().Any(a => a.Source == frontDoor.Resource))
            builder.Resource.Annotations.Add(new PulumiPrivateLinkTargetAnnotation(frontDoor.Resource));
        return builder;
    }
}
