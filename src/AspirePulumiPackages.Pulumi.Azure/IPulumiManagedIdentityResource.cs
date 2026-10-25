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
}
