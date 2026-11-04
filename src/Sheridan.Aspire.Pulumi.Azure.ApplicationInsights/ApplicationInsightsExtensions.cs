using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure.OperationalInsights;

namespace Sheridan.Aspire.Pulumi.Azure.ApplicationInsights;

/// <summary>Pulumi counterparts of the extension methods in the official <c>Aspire.Hosting.Azure.ApplicationInsights</c> package.</summary>
public static class ApplicationInsightsExtensions
{
    /// <summary>
    /// Adds an Application Insights component that the Pulumi stack provisions. Mirrors <c>AddAzureApplicationInsights</c>.
    /// Without a workspace, a Log Analytics workspace is created for it. <c>WithLocation</c>, <c>WithPulumiStack</c> and
    /// <c>WithExistingResourceGroup</c> come from the Azure.Base package.
    /// </summary>
    public static IResourceBuilder<PulumiApplicationInsightsResource> AddPulumiApplicationInsights(
        this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new PulumiApplicationInsightsResource(name)).ExcludeFromManifest();
    }

    /// <summary>Adds an Application Insights component linked to <paramref name="logAnalyticsWorkspace"/>.</summary>
    public static IResourceBuilder<PulumiApplicationInsightsResource> AddPulumiApplicationInsights(
        this IDistributedApplicationBuilder builder, string name, IResourceBuilder<LogAnalyticsWorkspaceResource>? logAnalyticsWorkspace)
    {
        var ai = builder.AddPulumiApplicationInsights(name);
        return logAnalyticsWorkspace is null ? ai : ai.WithLogAnalyticsWorkspace(logAnalyticsWorkspace);
    }

    /// <summary>Links the component to an existing Log Analytics workspace resource (must be in the same Pulumi stack). Mirrors <c>WithLogAnalyticsWorkspace</c>.</summary>
    public static IResourceBuilder<PulumiApplicationInsightsResource> WithLogAnalyticsWorkspace(
        this IResourceBuilder<PulumiApplicationInsightsResource> builder, IResourceBuilder<LogAnalyticsWorkspaceResource> logAnalyticsWorkspace)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(logAnalyticsWorkspace);
        builder.Resource.LogAnalyticsWorkspace = logAnalyticsWorkspace.Resource;
        return builder;
    }
}
