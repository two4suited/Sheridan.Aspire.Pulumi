using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace AspirePulumiPackages.Pulumi.Azure.OperationalInsights;

/// <summary>Pulumi counterparts of the extension methods in the official <c>Aspire.Hosting.Azure.OperationalInsights</c> package.</summary>
public static class LogAnalyticsWorkspaceExtensions
{
    /// <summary>
    /// Adds a Log Analytics workspace that the Pulumi stack provisions. Mirrors <c>AddAzureLogAnalyticsWorkspace</c>;
    /// <c>WithLocation</c>, <c>WithPulumiStack</c> and <c>WithExistingResourceGroup</c> come from the Azure.Base package.
    /// </summary>
    public static IResourceBuilder<LogAnalyticsWorkspaceResource> AddPulumiLogAnalyticsWorkspace(
        this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new LogAnalyticsWorkspaceResource(name)).ExcludeFromManifest();
    }
}
