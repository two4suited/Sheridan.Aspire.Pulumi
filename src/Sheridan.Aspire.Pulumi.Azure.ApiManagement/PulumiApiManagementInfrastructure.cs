using Sheridan.Aspire.Pulumi.Provisioning;

namespace Sheridan.Aspire.Pulumi.Azure.ApiManagement;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi API Management service (root:
/// <c>ApiManagementService</c>). Properties use azure-native names, e.g.
/// <c>ApiManagementService.Set("publicNetworkAccess", "Disabled")</c>.
/// </summary>
public sealed class PulumiApiManagementInfrastructure(PulumiInfrastructure inner, AzureApiManagementResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:apimanagement:ApiManagementService</c> resource.</summary>
    public PulumiResourceNode ApiManagementService => GetResource(owner.Id);
}
