using Aspire.Hosting.ApplicationModel;

namespace Sheridan.Aspire.Pulumi.Azure.Base;

/// <summary>
/// Marks a Container Apps or App Service environment as the target of a Private Link from <see cref="Source"/> (for example an Azure Front Door).
/// The environment disables public network access when it carries this annotation, like the official integration does for
/// <c>PrivateEndpointTargetAnnotation</c>.
/// </summary>
public sealed class PulumiPrivateLinkTargetAnnotation(IResource source) : IResourceAnnotation
{
    /// <summary>The resource that connects to the environment over Private Link.</summary>
    public IResource Source { get; } = source;
}
