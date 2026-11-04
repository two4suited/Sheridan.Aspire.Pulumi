using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Provisioning;

namespace Sheridan.Aspire.Pulumi.Azure.CognitiveServices;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi Azure OpenAI account. Port of the Azure.Provisioning
/// CognitiveServices overlay (root: <c>CognitiveServicesAccount</c>). Properties use azure-native names, e.g.
/// <c>CognitiveServicesAccount.Set("properties.publicNetworkAccess", "Disabled")</c>.
/// </summary>
public sealed class PulumiOpenAIInfrastructure(PulumiInfrastructure inner, AzureOpenAIResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:cognitiveservices:Account</c> (kind OpenAI).</summary>
    public PulumiResourceNode CognitiveServicesAccount => GetResource(owner.Id);

    /// <summary>The model deployments (<c>azure-native:cognitiveservices:Deployment</c>) of this account.</summary>
    public IEnumerable<PulumiResourceNode> Deployments => owner.Deployments.Select(d => GetResource($"{owner.Id}{d.Id}"));
}

public static class PulumiOpenAIInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the Azure OpenAI resources before the program is emitted.</summary>
    public static IResourceBuilder<AzureOpenAIResource> ConfigureInfrastructure(
        this IResourceBuilder<AzureOpenAIResource> builder, Action<PulumiOpenAIInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiOpenAIInfrastructure(infra, owner)));
    }
}
