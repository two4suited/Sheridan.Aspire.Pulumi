using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Provisioning;

namespace Sheridan.Aspire.Pulumi.Azure.EntraId;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi Entra ID application. Counterpart of the public Graph
/// provisioning models upstream (<c>MicrosoftGraphApplication</c>, <c>MicrosoftGraphServicePrincipal</c>, ...). Properties use
/// azuread provider names, e.g. <c>Application.Set("tags", new List&lt;object?&gt; { "x" })</c>.
/// </summary>
public sealed class PulumiEntraIdInfrastructure(PulumiInfrastructure inner, EntraIdApplicationResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azuread:index/application:Application</c>.</summary>
    public PulumiResourceNode Application => GetResource($"{owner.Id}App");

    /// <summary>The <c>azuread:index/servicePrincipal:ServicePrincipal</c>.</summary>
    public PulumiResourceNode ServicePrincipal => GetResource($"{owner.Id}Sp");

    /// <summary>Federated identity credentials (<c>ApplicationFederatedIdentityCredential</c>) of this registration.</summary>
    public IEnumerable<PulumiResourceNode> FederatedIdentityCredentials =>
        GetResources("azuread:index/applicationFederatedIdentityCredential:ApplicationFederatedIdentityCredential").Where(r => r.Id.StartsWith($"{owner.Id}Fic", StringComparison.Ordinal));
}

public static class PulumiEntraIdInfrastructureExtensions
{
    /// <summary>Customize the Entra ID resources before the Pulumi program is emitted. Not available for existing applications (nothing is provisioned).</summary>
    public static IResourceBuilder<EntraIdApplicationResource> ConfigureInfrastructure(
        this IResourceBuilder<EntraIdApplicationResource> builder, Action<PulumiEntraIdInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        if (owner.IsExistingApplication)
            throw new InvalidOperationException($"'{owner.Name}' is an existing application; there are no resources to configure.");
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiEntraIdInfrastructure(infra, owner)));
    }
}
