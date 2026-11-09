using Sheridan.Aspire.Pulumi.Provisioning;

namespace Sheridan.Aspire.Pulumi.Azure.ApiManagement;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi API Management service (root:
/// <c>ApiManagementService</c>). Properties use azure-native names, e.g.
/// <c>ApiManagementService.Set("publicNetworkAccess", "Disabled")</c>.
/// </summary>
public sealed class PulumiApiManagementInfrastructure(PulumiInfrastructure inner, AzureApiManagementResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:apimanagement:ApiManagementService</c> resource. Throws for services declared with AsExisting/PublishAsExisting.</summary>
    public PulumiResourceNode ApiManagementService => owner.ExistingServiceName is null
        ? GetResource(owner.Id)
        : throw new InvalidOperationException($"'{owner.Name}' is an existing API Management service; there is no ApiManagementService resource to configure.");
}
