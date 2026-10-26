using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.AppService;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi App Service environment. Port of the Azure.Provisioning
/// AppService overlay. Upstream has no no-argument lookup root: plans and sites are reached by identifier lookup, as here.
/// Properties use azure-native names, e.g. <c>GetWebApp("api").Set("siteConfig.alwaysOn", true)</c>.
/// </summary>
public sealed class PulumiAppServiceInfrastructure(PulumiInfrastructure inner, AzureAppServiceEnvironmentResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:web:AppServicePlan</c> shared by the environment's sites.</summary>
    public PulumiResourceNode GetAppServicePlan() => GetResource(owner.PlanResourceId);

    /// <summary>The <c>azure-native:web:WebApp</c> emitted for the compute resource with the given Aspire name.</summary>
    public PulumiResourceNode GetWebApp(string computeResourceName) => GetResource(AzureAppServiceEnvironmentResource.AppId(computeResourceName));
}

public static class PulumiAppServiceInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the App Service resources before the program is emitted.</summary>
    public static IResourceBuilder<AzureAppServiceEnvironmentResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureAppServiceEnvironmentResource> builder, Action<PulumiAppServiceInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiAppServiceInfrastructure(infra, owner)));
    }
}
