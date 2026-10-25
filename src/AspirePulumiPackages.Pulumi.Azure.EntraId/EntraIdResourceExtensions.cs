using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi;

namespace AspirePulumiPackages.Pulumi.Azure.EntraId;

/// <summary>Pulumi counterparts of the extension methods in the official <c>Aspire.Hosting.Azure.EntraId</c> package.</summary>
public static class EntraIdResourceExtensions
{
    /// <summary>Adds an Entra ID application registration (and service principal) that the Pulumi stack provisions. Mirrors <c>AddEntraIdApplication</c>.</summary>
    public static IResourceBuilder<EntraIdApplicationResource> AddPulumiEntraId(
        this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new EntraIdApplicationResource(name)).ExcludeFromManifest();
    }

    /// <summary>References an existing registration; nothing is provisioned. Mirrors <c>AddExistingEntraApplication</c>.</summary>
    public static IResourceBuilder<EntraIdApplicationResource> AddPulumiExistingEntraApplication(
        this IDistributedApplicationBuilder builder, string name, string tenantId, string clientId) =>
        builder.AddPulumiEntraId(name).AsExistingApplication(tenantId, clientId);

    /// <summary>Treats the resource as an existing registration. Mirrors <c>AsExistingApplication</c>.</summary>
    public static IResourceBuilder<EntraIdApplicationResource> AsExistingApplication(
        this IResourceBuilder<EntraIdApplicationResource> builder, string tenantId, string clientId)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        if (!Guid.TryParse(tenantId, out _)) throw new ArgumentException("The tenant ID must be a GUID.", nameof(tenantId));
        if (!Guid.TryParse(clientId, out _)) throw new ArgumentException("The client ID must be a GUID.", nameof(clientId));
        builder.Resource.IsExistingApplication = true;
        builder.Resource.ExistingTenantId = tenantId;
        builder.Resource.ExistingClientId = clientId;
        return builder;
    }

    public static IResourceBuilder<EntraIdApplicationResource> WithInstance(
        this IResourceBuilder<EntraIdApplicationResource> builder, string instance)
    {
        ArgumentException.ThrowIfNullOrEmpty(instance);
        if (!Uri.TryCreate(instance, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("The instance must be an absolute https URL.", nameof(instance));
        builder.Resource.Instance = instance;
        return builder;
    }

    public static IResourceBuilder<EntraIdApplicationResource> WithSignInAudience(
        this IResourceBuilder<EntraIdApplicationResource> builder, EntraIdSignInAudience signInAudience)
    {
        if (!Enum.IsDefined(signInAudience)) throw new ArgumentOutOfRangeException(nameof(signInAudience));
        builder.Resource.SignInAudience = signInAudience;
        return builder;
    }

    public static IResourceBuilder<EntraIdApplicationResource> WithRedirectUris(
        this IResourceBuilder<EntraIdApplicationResource> builder, params string[] redirectUris)
    {
        ArgumentNullException.ThrowIfNull(redirectUris);
        foreach (var uri in redirectUris)
        {
            ArgumentException.ThrowIfNullOrEmpty(uri);
            builder.Resource.RedirectUris.Add(uri);
        }
        return builder;
    }

    /// <summary>Adds object IDs as owners of the application registration.</summary>
    public static IResourceBuilder<EntraIdApplicationResource> WithApplicationOwners(
        this IResourceBuilder<EntraIdApplicationResource> builder, params string[] ownerObjectIds)
    {
        builder.Resource.ApplicationOwners.AddRange(ValidateOwners(ownerObjectIds));
        return builder;
    }

    /// <summary>Adds object IDs as owners of the service principal.</summary>
    public static IResourceBuilder<EntraIdApplicationResource> WithServicePrincipalOwners(
        this IResourceBuilder<EntraIdApplicationResource> builder, params string[] ownerObjectIds)
    {
        builder.Resource.ServicePrincipalOwners.AddRange(ValidateOwners(ownerObjectIds));
        return builder;
    }

    /// <summary>Adds object IDs as owners of both the application registration and the service principal.</summary>
    public static IResourceBuilder<EntraIdApplicationResource> WithOwners(
        this IResourceBuilder<EntraIdApplicationResource> builder, params string[] ownerObjectIds) =>
        builder.WithApplicationOwners(ownerObjectIds).WithServicePrincipalOwners(ownerObjectIds);

    /// <summary>Enables ID token issuance (implicit grant) for the registration.</summary>
    public static IResourceBuilder<EntraIdApplicationResource> WithIdTokenIssuance(
        this IResourceBuilder<EntraIdApplicationResource> builder, bool enabled = true)
    {
        builder.Resource.EnableIdTokenIssuance = enabled;
        return builder;
    }

    public static IResourceBuilder<EntraIdApplicationResource> WithPulumiStack(
        this IResourceBuilder<EntraIdApplicationResource> builder, IResourceBuilder<PulumiStackResource> stack)
    {
        builder.Resource.Stack = stack.Resource;
        return builder;
    }

    /// <summary>
    /// Injects the registration's configuration as environment variables. .NET projects get the
    /// Microsoft.Identity.Web names (<c>AzureAd__Instance</c>, <c>AzureAd__TenantId</c>, <c>AzureAd__ClientId</c>);
    /// other resources get an uppercase resource-name prefix with a single underscore separator.
    /// </summary>
    public static IResourceBuilder<TDestination> WithReference<TDestination>(
        this IResourceBuilder<TDestination> builder, IResourceBuilder<EntraIdApplicationResource> source)
        where TDestination : IResourceWithEnvironment
    {
        var app = source.Resource;
        var isDotNet = builder.Resource is ProjectResource;
        var prefix = isDotNet ? "AzureAd" : app.Name.ToUpperInvariant().Replace('-', '_');
        var sep = isDotNet ? "__" : "_";
        return builder.WithEnvironment(ctx =>
        {
            ctx.EnvironmentVariables[$"{prefix}{sep}Instance"] = app.Instance;
            ctx.EnvironmentVariables[$"{prefix}{sep}TenantId"] = app.TenantIdValue;
            ctx.EnvironmentVariables[$"{prefix}{sep}ClientId"] = app.ClientIdValue;
        });
    }

    private static string[] ValidateOwners(string[] ownerObjectIds)
    {
        ArgumentNullException.ThrowIfNull(ownerObjectIds);
        foreach (var id in ownerObjectIds)
        {
            if (!Guid.TryParse(id, out _)) throw new ArgumentException($"Owner '{id}' is not a valid object ID (GUID).", nameof(ownerObjectIds));
        }
        return ownerObjectIds;
    }
}
