using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.FrontDoor;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi Front Door. Port of the Azure.Provisioning CDN overlay
/// for the types the integration creates (profile, endpoint, origin group, origin, route). Properties use azure-native names, e.g.
/// <c>GetRoute("api").Set("patternsToMatch", new[] { "/api/*" })</c>.
/// </summary>
public sealed class PulumiFrontDoorInfrastructure(PulumiInfrastructure inner, AzureFrontDoorResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:cdn:Profile</c> resource.</summary>
    public PulumiResourceNode Profile => GetResource(owner.Id);

    /// <summary>The <c>azure-native:cdn:AFDEndpoint</c> for the named origin resource (its Aspire resource name).</summary>
    public PulumiResourceNode GetEndpoint(string originName) => GetResource(owner.EndpointId(originName));

    /// <summary>The <c>azure-native:cdn:AFDOriginGroup</c> for the named origin resource.</summary>
    public PulumiResourceNode GetOriginGroup(string originName) => GetResource(owner.OriginGroupId(originName));

    /// <summary>The <c>azure-native:cdn:AFDOrigin</c> for the named origin resource.</summary>
    public PulumiResourceNode GetOrigin(string originName) => GetResource(owner.OriginId(originName));

    /// <summary>The <c>azure-native:cdn:Route</c> for the named origin resource.</summary>
    public PulumiResourceNode GetRoute(string originName) => GetResource(owner.RouteId(originName));
}
