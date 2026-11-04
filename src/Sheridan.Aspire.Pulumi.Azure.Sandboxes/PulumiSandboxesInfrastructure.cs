using Sheridan.Aspire.Pulumi.Provisioning;

namespace Sheridan.Aspire.Pulumi.Azure.Sandboxes;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi sandbox group. Port of the Azure.Provisioning
/// overlay (root: <c>SandboxGroup</c>). The group is an <c>azure-native:resources:Resource</c> of type
/// <c>Microsoft.App/sandboxGroups</c>, so its settings live under <c>properties.</c> and the ARM api-version is the
/// <c>apiVersion</c> input, e.g. <c>SandboxGroup.Set("properties.networkProfile.subnets", ...)</c>.
/// </summary>
public sealed class PulumiSandboxesInfrastructure(PulumiInfrastructure inner, AzureSandboxGroupResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>Microsoft.App/sandboxGroups</c> resource; throws for an existing group (<c>PublishAsExisting</c>), which has no emitted resource.</summary>
    public PulumiResourceNode SandboxGroup => owner.IsExisting
        ? throw new InvalidOperationException($"'{owner.Name}' is an existing sandbox group; no sandbox group resource is emitted to configure.")
        : GetResource(owner.Id);

    /// <summary>The group's own container registry; throws when <c>WithAzureContainerRegistry</c> supplied one.</summary>
    public PulumiResourceNode ContainerRegistry => owner.OwnsRegistry
        ? GetResource(owner.RegistryId)
        : throw new InvalidOperationException($"'{owner.Name}' uses an external container registry; configure that resource instead.");

    /// <summary>The group's own image-pull identity; throws when <c>WithAcrPullIdentity</c> supplied one.</summary>
    public PulumiResourceNode ImagePullIdentity => owner.OwnsPullIdentity
        ? GetResource(owner.PullIdentityId)
        : throw new InvalidOperationException($"'{owner.Name}' uses an external image-pull identity; configure that resource instead.");
}
