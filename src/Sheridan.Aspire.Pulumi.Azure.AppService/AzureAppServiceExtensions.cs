using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure.AppService;

namespace Aspire.Hosting;

/// <summary>Pulumi counterparts of the extension methods in the official <c>Aspire.Hosting.Azure.AppService</c> package.</summary>
public static class AzureAppServiceExtensions
{
    /// <summary>Adds an Azure App Service environment provisioned by Pulumi. Mirrors <c>AddAzureAppServiceEnvironment</c>.</summary>
    public static IResourceBuilder<AzureAppServiceEnvironmentResource> AddPulumiAzureAppServiceEnvironment(
        this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new AzureAppServiceEnvironmentResource(name)).ExcludeFromManifest();
    }

    /// <summary>Alias for <see cref="AddPulumiAzureAppServiceEnvironment"/>.</summary>
    public static IResourceBuilder<AzureAppServiceEnvironmentResource> AddPulumiAppServiceEnvironment(
        this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiAzureAppServiceEnvironment(name);

    /// <summary>Enables or disables the Aspire dashboard site in the environment. Mirrors <c>WithDashboard</c> (default enabled).</summary>
    public static IResourceBuilder<AzureAppServiceEnvironmentResource> WithDashboard(
        this IResourceBuilder<AzureAppServiceEnvironmentResource> builder, bool enable = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Resource.EnableDashboard = enable;
        return builder;
    }

    /// <summary>Creates an Application Insights component (with a Log Analytics workspace) and wires every web app to it. Mirrors <c>WithAzureApplicationInsights</c>.</summary>
    public static IResourceBuilder<AzureAppServiceEnvironmentResource> WithAzureApplicationInsights(
        this IResourceBuilder<AzureAppServiceEnvironmentResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Resource.EnableApplicationInsights = true;
        return builder;
    }

    /// <summary>Sets the App Service plan SKU (default P0V3 / Premium).</summary>
    public static IResourceBuilder<AzureAppServiceEnvironmentResource> WithSku(
        this IResourceBuilder<AzureAppServiceEnvironmentResource> builder, string skuName, string skuTier)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(skuName);
        ArgumentException.ThrowIfNullOrEmpty(skuTier);
        builder.Resource.SkuName = skuName;
        builder.Resource.SkuTier = skuTier;
        return builder;
    }
}
