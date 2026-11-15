using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Sheridan.Aspire.Pulumi.Azure.ApiManagement;

/// <summary>How an API Management service is deployed into a virtual network (<c>virtualNetworkType</c>).</summary>
public enum AzureApiManagementVirtualNetworkType
{
    /// <summary>The gateway keeps a public endpoint and can reach the VNet.</summary>
    External,
    /// <summary>The gateway is reachable only from inside the VNet.</summary>
    Internal,
}

/// <summary>Networking options for <see cref="AzureApiManagementResource"/>. Subnet and private endpoint wiring use the Network package (<c>WithDelegatedSubnet</c>, <c>AddPrivateEndpoint</c>).</summary>
public static class AzureApiManagementNetworkingExtensions
{
    /// <summary>
    /// Sets the VNet mode used together with <c>WithDelegatedSubnet(subnet)</c>; External when this is not called.
    /// Developer, Premium and PremiumV2 support both modes; StandardV2 supports External only (outbound integration).
    /// </summary>
    public static IResourceBuilder<AzureApiManagementResource> WithVirtualNetworkType(
        this IResourceBuilder<AzureApiManagementResource> builder, AzureApiManagementVirtualNetworkType type)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Resource.VirtualNetworkType = type;
        return builder;
    }

    /// <summary>
    /// Sets <c>publicNetworkAccess</c> on the service. Azure only accepts <c>Disabled</c> once a private endpoint exists, so a new
    /// service that is to be private-only must first be created with public access enabled (the default), then switched off.
    /// </summary>
    public static IResourceBuilder<AzureApiManagementResource> WithPublicNetworkAccess(
        this IResourceBuilder<AzureApiManagementResource> builder, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Resource.PublicNetworkAccess = enabled;
        return builder;
    }
}
