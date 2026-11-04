using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
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
}
