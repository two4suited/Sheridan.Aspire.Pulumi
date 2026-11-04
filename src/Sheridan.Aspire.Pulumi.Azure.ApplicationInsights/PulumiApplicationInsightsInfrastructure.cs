using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Provisioning;

namespace Sheridan.Aspire.Pulumi.Azure.ApplicationInsights;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi Application Insights component. Port of the Azure.Provisioning
/// ApplicationInsights overlay (root: <c>ApplicationInsightsComponent</c>). Properties use azure-native names, e.g.
/// <c>ApplicationInsightsComponent.Set("retentionInDays", 30)</c>.
/// </summary>
public sealed class PulumiApplicationInsightsInfrastructure(PulumiInfrastructure inner, PulumiApplicationInsightsResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:applicationinsights:Component</c>.</summary>
    public PulumiResourceNode ApplicationInsightsComponent => GetResource(owner.Id);

    /// <summary>The implicit <c>azure-native:operationalinsights:Workspace</c> created alongside the component, or null when an explicit workspace was supplied.</summary>
    public PulumiResourceNode? ImplicitLogAnalyticsWorkspace => TryGetResource($"{owner.Id}Law");
}

public static class PulumiApplicationInsightsInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the Application Insights resources before the program is emitted.</summary>
    public static IResourceBuilder<PulumiApplicationInsightsResource> ConfigureInfrastructure(
        this IResourceBuilder<PulumiApplicationInsightsResource> builder, Action<PulumiApplicationInsightsInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiApplicationInsightsInfrastructure(infra, owner)));
    }
}
