using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure.Base;

namespace Sheridan.Aspire.Pulumi.Azure.Sandboxes;

/// <summary>Deployment target that represents the sandbox (and its disk image and ports) created in a sandbox group for a compute resource.</summary>
public sealed class AzureSandboxContainerResource(string name, IResource targetResource, AzureSandboxGroupResource parent)
    : Resource(name), IResourceWithParent<AzureSandboxGroupResource>
{
    /// <summary>The compute resource deployed as a sandbox.</summary>
    public IResource TargetResource { get; } = targetResource;

    /// <summary>The sandbox group the sandbox lives in.</summary>
    public AzureSandboxGroupResource Parent { get; } = parent;
}

/// <summary>Sandbox runtime options for a compute resource (see <c>PublishAsAzureSandbox</c>).</summary>
internal sealed class AzureSandboxContainerOptionsAnnotation(AzureSandboxOptions options) : IResourceAnnotation
{
    public AzureSandboxOptions Options { get; } = options;
}

/// <summary>Runs the compute resource's sandbox as a Pulumi user-assigned identity (the equivalent of <c>WithAzureUserAssignedIdentity</c>).</summary>
internal sealed class AzureSandboxIdentityAnnotation(PulumiAzureUserAssignedIdentityResource identity) : IResourceAnnotation
{
    public PulumiAzureUserAssignedIdentityResource Identity { get; } = identity;
}
