namespace AspirePulumiPackages.Pulumi.Azure.Base;

/// <summary>A Pulumi-provisioned Azure resource whose Azure names can be composed (or pinned) by the naming layer.</summary>
public interface IPulumiAzureNamed : IPulumiAzureResource
{
    /// <summary>Explicit names keyed by (kind, suffix); see <see cref="PulumiAzureExtensions.WithAzureName{T}"/>.</summary>
    IDictionary<(AzureNameKind Kind, string? Suffix), string> NameOverrides { get; }
}

public static class AzureNamingExtensions
{
    /// <summary>
    /// Returns the override registered for (kind, suffix) or composes <c>{project}-{resource}[-{suffix}]-{stack}</c> from
    /// the stack's <see cref="PulumiStackResource.ProjectName"/>, the Aspire resource name and the stack name.
    /// </summary>
    public static string NameFor(this IPulumiAzureNamed resource, PulumiStackResource stack, AzureNameKind kind, string? suffix = null) =>
        resource.NameOverrides.TryGetValue((kind, suffix), out var explicitName)
            ? explicitName
            : AzureNaming.Compose(kind, stack.ProjectName, resource.Name, stack.StackName, suffix);
}
