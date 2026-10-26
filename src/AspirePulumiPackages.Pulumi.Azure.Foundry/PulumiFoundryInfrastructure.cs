using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.Foundry;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi Foundry account. Properties use azure-native names, e.g.
/// <c>CognitiveServicesAccount.Set("properties.publicNetworkAccess", "Disabled")</c>.
/// </summary>
public sealed class PulumiFoundryInfrastructure(PulumiInfrastructure inner, FoundryResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:cognitiveservices:Account</c> (kind AIServices).</summary>
    public PulumiResourceNode CognitiveServicesAccount => GetResource(owner.Id);

    /// <summary>The account capability host (<c>azure-native:cognitiveservices:AccountCapabilityHost</c>).</summary>
    public PulumiResourceNode CapabilityHost => GetResource($"{owner.Id}Caphost");

    /// <summary>The model deployments (<c>azure-native:cognitiveservices:Deployment</c>).</summary>
    public IEnumerable<PulumiResourceNode> Deployments => owner.Deployments.Select(d => GetResource($"{owner.Id}{d.Id}"));

    /// <summary>The projects (<c>azure-native:cognitiveservices:Project</c>).</summary>
    public IEnumerable<PulumiResourceNode> Projects => owner.Projects.Select(p => GetResource($"{owner.Id}{p.Id}"));
}

public static class PulumiFoundryInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the Foundry resources before the program is emitted.</summary>
    public static IResourceBuilder<FoundryResource> ConfigureInfrastructure(
        this IResourceBuilder<FoundryResource> builder, Action<PulumiFoundryInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiFoundryInfrastructure(infra, owner)));
    }
}
