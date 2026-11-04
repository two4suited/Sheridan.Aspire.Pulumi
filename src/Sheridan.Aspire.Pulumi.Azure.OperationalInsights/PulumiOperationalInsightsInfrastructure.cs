using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Provisioning;

namespace Sheridan.Aspire.Pulumi.Azure.OperationalInsights;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi Log Analytics workspace. Port of the Azure.Provisioning
/// OperationalInsights overlay (root: <c>OperationalInsightsWorkspace</c>). Properties use azure-native names, e.g.
/// <c>OperationalInsightsWorkspace.Set("retentionInDays", 90)</c>.
/// </summary>
public sealed class PulumiOperationalInsightsInfrastructure(PulumiInfrastructure inner, LogAnalyticsWorkspaceResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:operationalinsights:Workspace</c>.</summary>
    public PulumiResourceNode OperationalInsightsWorkspace => GetResource(owner.Id);
}

public static class PulumiOperationalInsightsInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the workspace before the program is emitted.</summary>
    public static IResourceBuilder<LogAnalyticsWorkspaceResource> ConfigureInfrastructure(
        this IResourceBuilder<LogAnalyticsWorkspaceResource> builder, Action<PulumiOperationalInsightsInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiOperationalInsightsInfrastructure(infra, owner)));
    }
}
