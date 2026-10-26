using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.Foundry;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi Foundry account. Properties use azure-native names, e.g.
/// <c>CognitiveServicesAccount.Set("properties.publicNetworkAccess", "Disabled")</c>.
/// </summary>
public sealed class PulumiFoundryInfrastructure(PulumiInfrastructure inner, FoundryResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:cognitiveservices:Account</c> (kind AIServices); not available for an existing account (<c>PublishAsExisting</c>).</summary>
    public PulumiResourceNode CognitiveServicesAccount => owner.ExistingAccountName is null
        ? GetResource(owner.Id)
        : throw new InvalidOperationException($"'{owner.Name}' is an existing account (PublishAsExisting): no account resource is created for it.");

    /// <summary>The account capability host (<c>azure-native:cognitiveservices/v20251201:AccountCapabilityHost</c>; its settings live under <c>properties.</c>).</summary>
    public PulumiResourceNode CapabilityHost => GetResource($"{owner.Id}Caphost");

    /// <summary>The model deployments (<c>azure-native:cognitiveservices:Deployment</c>).</summary>
    public IEnumerable<PulumiResourceNode> Deployments => owner.Deployments.Select(d => GetResource($"{owner.Id}{d.Id}"));

    /// <summary>The projects (<c>azure-native:cognitiveservices:Project</c>).</summary>
    public IEnumerable<PulumiResourceNode> Projects => owner.Projects.Select(p => GetResource($"{owner.Id}{p.Id}"));

    /// <summary>The project capability hosts (<c>azure-native:cognitiveservices/v20251201:ProjectCapabilityHost</c>) of the projects that have one.</summary>
    public IEnumerable<PulumiResourceNode> ProjectCapabilityHosts => owner.Projects.Where(p => p.CapabilityHost is not null)
        .Select(p => GetResource($"{owner.Id}{p.Id}{FoundryProjectResource.CapabilityHostIdSuffix}"));

    /// <summary>The project connections (<c>azure-native:cognitiveservices:ProjectConnection</c>/<c>AccountConnection</c>) added with AddConnection, WithKeyVault and AddBingGroundingConnection.</summary>
    public IEnumerable<PulumiResourceNode> Connections => owner.Projects.SelectMany(p => p.Connections.Select(c => GetResource($"{owner.Id}{p.Id}{c.Id}")));
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
