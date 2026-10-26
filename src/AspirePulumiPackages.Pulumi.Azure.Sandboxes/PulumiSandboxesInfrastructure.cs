using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.Sandboxes;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi sandbox group. Port of the Azure.Provisioning
/// overlay (root: <c>SandboxGroup</c>). Properties use azure-native names, e.g.
/// <c>SandboxGroup.Set("properties.networkProfile.subnets", ...)</c>.
/// </summary>
public sealed class PulumiSandboxesInfrastructure(PulumiInfrastructure inner, AzureSandboxGroupResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:containerinstance:SandboxGroup</c> resource.</summary>
    public PulumiResourceNode SandboxGroup => GetResource(owner.Id);

    /// <summary>The group's own container registry; throws when <c>WithAzureContainerRegistry</c> supplied one.</summary>
    public PulumiResourceNode ContainerRegistry => owner.OwnsRegistry
        ? GetResource(owner.RegistryId)
        : throw new InvalidOperationException($"'{owner.Name}' uses an external container registry; configure that resource instead.");

    /// <summary>The group's own image-pull identity; throws when <c>WithAcrPullIdentity</c> supplied one.</summary>
    public PulumiResourceNode ImagePullIdentity => owner.OwnsPullIdentity
        ? GetResource(owner.PullIdentityId)
        : throw new InvalidOperationException($"'{owner.Name}' uses an external image-pull identity; configure that resource instead.");
}
