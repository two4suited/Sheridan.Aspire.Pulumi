using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.SignalR;

/// <summary>Pulumi equivalents of the Aspire.Hosting.Azure.SignalR extension methods.</summary>
public static class AzureSignalRExtensions
{
    /// <summary>Adds an Azure SignalR Service provisioned by the Pulumi stack (the equivalent of AddAzureSignalR).</summary>
    public static IResourceBuilder<AzureSignalRResource> AddPulumiSignalR(
        this IDistributedApplicationBuilder builder, string name, AzureSignalRServiceMode serviceMode = AzureSignalRServiceMode.Default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!Enum.IsDefined(serviceMode)) throw new ArgumentException($"'{serviceMode}' is not a valid service mode.", nameof(serviceMode));
        return builder.AddResource(new AzureSignalRResource(name) { ServiceMode = serviceMode }).ExcludeFromManifest();
    }

    /// <summary>Alias of <see cref="AddPulumiSignalR"/> following the <c>AddPulumiAzureX</c> naming used by the other Azure integrations.</summary>
    public static IResourceBuilder<AzureSignalRResource> AddPulumiAzureSignalR(
        this IDistributedApplicationBuilder builder, string name, AzureSignalRServiceMode serviceMode = AzureSignalRServiceMode.Default) =>
        builder.AddPulumiSignalR(name, serviceMode);

    /// <summary>Overrides the SKU (default Free_F1 with capacity 1, as in the official integration), e.g. <c>Standard_S1</c>.</summary>
    public static IResourceBuilder<AzureSignalRResource> WithSku(this IResourceBuilder<AzureSignalRResource> builder, string sku, int capacity = 1)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(sku);
        builder.Resource.Sku = sku;
        builder.Resource.Capacity = capacity;
        return builder;
    }

    /// <summary>
    /// Replaces the default role assignment (SignalR App Server) for the compute environment identity hosting
    /// <paramref name="builder"/> with the given roles on the service. Mirrors <c>WithRoleAssignments</c>.
    /// </summary>
    public static IResourceBuilder<T> WithRoleAssignments<T>(
        this IResourceBuilder<T> builder, IResourceBuilder<AzureSignalRResource> target, params AzureSignalRRole[] roles)
        where T : IResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(roles);
        foreach (var role in roles)
            if (!Enum.IsDefined(role)) throw new ArgumentException($"'{role}' is not a valid SignalR role.", nameof(roles));
        return builder.WithAnnotation(new AzureSignalRRoleAnnotation(target.Resource, roles));
    }

    /// <summary>Pulumi equivalent of <c>ConfigureInfrastructure</c>: customize the SignalR resources before the program is emitted.</summary>
    public static IResourceBuilder<AzureSignalRResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureSignalRResource> builder, Action<PulumiSignalRInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiSignalRInfrastructure(infra, owner)));
    }
}
