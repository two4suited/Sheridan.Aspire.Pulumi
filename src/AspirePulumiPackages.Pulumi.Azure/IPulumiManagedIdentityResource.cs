using Aspire.Hosting.ApplicationModel;

namespace AspirePulumiPackages.Pulumi.Azure;

/// <summary>
/// A Pulumi-provisioned resource with a managed identity that other resources in the same stack can grant
/// data-plane roles to (e.g. a compute environment's identity being granted access to a database).
/// </summary>
public interface IPulumiManagedIdentityResource : IResource
{
    /// <summary>Pulumi property-access expression for the identity's principal id (e.g. "${FooIdentity.principalId}"). Only valid within the same Pulumi program.</summary>
    string PrincipalIdExpression { get; }

    /// <summary>Pulumi expression for the identity's name (used e.g. as a database principal name); null when not exposed.</summary>
    string? PrincipalNameExpression => null;

    /// <summary>Pulumi expression for the identity's client id; null when not exposed.</summary>
    string? ClientIdExpression => null;
}
