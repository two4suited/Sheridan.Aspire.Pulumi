using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi;
using Sheridan.Aspire.Pulumi.Azure;

namespace Sheridan.Aspire.Pulumi.Azure.EntraId;

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
        if (builder.Resource.FirstRegistrationOnlySetting() is { } already)
            throw new InvalidOperationException($"'{builder.Resource.Name}' has {already} configured, which only applies to a registration this AppHost provisions. Remove it, or don't convert the resource with AsExistingApplication.");
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
        RejectOnExisting(builder, "WithRedirectUris");
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
        RejectOnExisting(builder, "WithApplicationOwners");
        builder.Resource.ApplicationOwners.AddRange(ValidateOwners(ownerObjectIds));
        return builder;
    }

    /// <summary>Adds object IDs as owners of the service principal.</summary>
    public static IResourceBuilder<EntraIdApplicationResource> WithServicePrincipalOwners(
        this IResourceBuilder<EntraIdApplicationResource> builder, params string[] ownerObjectIds)
    {
        RejectOnExisting(builder, "WithServicePrincipalOwners");
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
        RejectOnExisting(builder, "WithIdTokenIssuance");
        builder.Resource.EnableIdTokenIssuance = enabled;
        return builder;
    }

    /// <summary>Pins the app registration's display name (1-256 characters) instead of <c>{app}-{resource}-{stack}</c>.</summary>
    public static IResourceBuilder<EntraIdApplicationResource> WithDisplayName(
        this IResourceBuilder<EntraIdApplicationResource> builder, string displayName)
    {
        builder.Resource.DisplayName = displayName;
        return builder;
    }

    /// <summary>
    /// Sets the identifier URI to <c>api://{clientId}</c>. The client ID only exists once the application does, so the URI is
    /// registered by an <c>ApplicationIdentifierUri</c> resource. On an existing registration use <see cref="WithApplicationIdUri(IResourceBuilder{EntraIdApplicationResource}, string)"/>.
    /// </summary>
    public static IResourceBuilder<EntraIdApplicationResource> WithApplicationIdUri(
        this IResourceBuilder<EntraIdApplicationResource> builder)
    {
        RejectOnExisting(builder, "WithApplicationIdUri()");
        builder.Resource.UseDefaultApplicationIdUri = true;
        return builder;
    }

    /// <summary>Sets an explicit identifier URI. On an existing registration it only tells consumers which URI to request tokens for.</summary>
    public static IResourceBuilder<EntraIdApplicationResource> WithApplicationIdUri(
        this IResourceBuilder<EntraIdApplicationResource> builder, string identifierUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifierUri);
        if (!Uri.TryCreate(identifierUri, UriKind.Absolute, out _)) throw new ArgumentException("The identifier URI must be an absolute URI.", nameof(identifierUri));
        builder.Resource.ApplicationIdUri = identifierUri;
        return builder;
    }

    /// <summary>
    /// Exposes a delegated permission scope. The scope ID is a stable GUID derived from the registration and scope name unless
    /// <see cref="EntraIdScope.Id"/> is set. Graph rejects removing an enabled scope: set <see cref="EntraIdScope.IsEnabled"/> to false, deploy, then remove it.
    /// </summary>
    public static IResourceBuilder<EntraIdApplicationResource> WithScope(
        this IResourceBuilder<EntraIdApplicationResource> builder, string name, string description, Action<EntraIdScope>? configure = null)
    {
        var scope = new EntraIdScope(name, description);
        configure?.Invoke(scope);
        if (builder.Resource.Scopes.Any(s => s.Name == scope.Name)) throw new InvalidOperationException($"'{builder.Resource.Name}' already declares a scope named '{scope.Name}'.");
        builder.Resource.Scopes.Add(scope);
        return builder;
    }

    /// <summary>Pre-authorizes <paramref name="client"/> for the given scopes of this API, so users are not asked to consent.</summary>
    public static IResourceBuilder<EntraIdApplicationResource> WithPreAuthorizedApplication(
        this IResourceBuilder<EntraIdApplicationResource> builder, IResourceBuilder<EntraIdApplicationResource> client, params string[] scopes)
    {
        RejectOnExisting(builder, "WithPreAuthorizedApplication");
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(scopes);
        if (scopes.Length == 0) throw new ArgumentException("At least one scope is required.", nameof(scopes));
        foreach (var scope in scopes) _ = builder.Resource.GetScopeId(scope);
        builder.Resource.PreAuthorizedApplications.Add((client.Resource, scopes));
        return builder;
    }

    /// <summary>Requests delegated permissions (scopes) on another registration in this AppHost. Nothing is consented until <see cref="WithAdminConsent"/>.</summary>
    public static IResourceBuilder<EntraIdApplicationResource> WithApiPermissions(
        this IResourceBuilder<EntraIdApplicationResource> builder, IResourceBuilder<EntraIdApplicationResource> api, params string[] scopes)
    {
        RejectOnExisting(builder, "WithApiPermissions");
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(scopes);
        foreach (var scope in scopes) _ = api.Resource.GetScopeId(scope);
        var request = builder.Resource.ApiPermissions.FirstOrDefault(r => r.Modeled == api.Resource);
        if (request is null) builder.Resource.ApiPermissions.Add(request = new ApiPermissionRequest { Modeled = api.Resource });
        request.Permissions.AddRange(scopes.Select(EntraIdApiPermission.Delegated));
        return builder;
    }

    /// <summary>Requests permissions on an API that is not modeled in the AppHost (Microsoft Graph, Azure Resource Manager, Azure Storage or any application ID).</summary>
    public static IResourceBuilder<EntraIdApplicationResource> WithApiPermissions(
        this IResourceBuilder<EntraIdApplicationResource> builder, EntraIdApi api, params EntraIdApiPermission[] permissions)
    {
        RejectOnExisting(builder, "WithApiPermissions");
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(permissions);
        var request = builder.Resource.ApiPermissions.FirstOrDefault(r => r.WellKnown == api);
        if (request is null) builder.Resource.ApiPermissions.Add(request = new ApiPermissionRequest { WellKnown = api });
        request.Permissions.AddRange(permissions);
        return builder;
    }

    /// <summary>
    /// Grants tenant-wide admin consent for every permission requested with <c>WithApiPermissions</c>. This is the only call that
    /// grants anything; the deploying principal needs <c>DelegatedPermissionGrant.ReadWrite.All</c> / <c>AppRoleAssignment.ReadWrite.All</c> and <c>Application.Read.All</c>.
    /// </summary>
    public static IResourceBuilder<EntraIdApplicationResource> WithAdminConsent(
        this IResourceBuilder<EntraIdApplicationResource> builder)
    {
        RejectOnExisting(builder, "WithAdminConsent");
        builder.Resource.AdminConsent = true;
        return builder;
    }

    /// <summary>Declares an application role. The role ID is a stable GUID derived from the registration and role value unless <see cref="EntraIdAppRole.Id"/> is set.</summary>
    public static IResourceBuilder<EntraIdApplicationResource> WithAppRole(
        this IResourceBuilder<EntraIdApplicationResource> builder, EntraIdAppRole role)
    {
        RejectOnExisting(builder, "WithAppRole");
        ArgumentNullException.ThrowIfNull(role);
        if (builder.Resource.AppRoles.Any(r => r.Value == role.Value)) throw new InvalidOperationException($"'{builder.Resource.Name}' already declares an app role '{role.Value}'.");
        builder.Resource.AppRoles.Add(role);
        return builder;
    }

    /// <summary>Grants the app role <paramref name="role"/> of this registration to a managed identity (an <c>appRoleAssignment</c>). Removing the call does not revoke the grant.</summary>
    public static IResourceBuilder<EntraIdApplicationResource> PublishWithAppRoleAssignment(
        this IResourceBuilder<EntraIdApplicationResource> builder, IResourceBuilder<IPulumiManagedIdentityResource> identity, string role)
    {
        RejectOnExisting(builder, "PublishWithAppRoleAssignment");
        ArgumentNullException.ThrowIfNull(identity);
        _ = builder.Resource.GetRoleId(role);
        builder.Resource.AppRoleAssignments.Add((role, identity.Resource));
        return builder;
    }

    /// <summary>
    /// Adds a federated identity credential that trusts a managed identity, so the workload authenticates as the registration with
    /// no secret. The identity must be created by the same Pulumi program as the registration.
    /// </summary>
    public static IResourceBuilder<EntraIdApplicationResource> PublishWithFederatedManagedIdentityCredential(
        this IResourceBuilder<EntraIdApplicationResource> builder, IResourceBuilder<IPulumiManagedIdentityResource> identity)
    {
        RejectOnExisting(builder, "PublishWithFederatedManagedIdentityCredential");
        ArgumentNullException.ThrowIfNull(identity);
        builder.Resource.FederatedIdentities.Add(identity.Resource);
        return builder;
    }

    /// <summary>Config only: tells consumers which managed identity client ID is trusted by a federated credential created elsewhere (<c>ManagedIdentityClientId</c>). Allowed on existing registrations.</summary>
    public static IResourceBuilder<EntraIdApplicationResource> WithFederatedManagedIdentityCredential(
        this IResourceBuilder<EntraIdApplicationResource> builder, string managedIdentityClientId)
    {
        if (!Guid.TryParse(managedIdentityClientId, out _)) throw new ArgumentException("The client ID must be a GUID.", nameof(managedIdentityClientId));
        builder.Resource.FederatedManagedIdentityClientId = managedIdentityClientId;
        return builder;
    }

    /// <summary>Adds application tags.</summary>
    public static IResourceBuilder<EntraIdApplicationResource> WithTags(
        this IResourceBuilder<EntraIdApplicationResource> builder, params string[] tags)
    {
        RejectOnExisting(builder, "WithTags");
        ArgumentNullException.ThrowIfNull(tags);
        builder.Resource.Tags.AddRange(tags);
        return builder;
    }

    /// <summary>Sets the service management reference (for example an ITSM application ID).</summary>
    public static IResourceBuilder<EntraIdApplicationResource> WithServiceManagementReference(
        this IResourceBuilder<EntraIdApplicationResource> builder, string reference)
    {
        RejectOnExisting(builder, "WithServiceManagementReference");
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        builder.Resource.ServiceManagementReference = reference;
        return builder;
    }

    /// <summary>Sets the application description.</summary>
    public static IResourceBuilder<EntraIdApplicationResource> WithDescription(
        this IResourceBuilder<EntraIdApplicationResource> builder, string description)
    {
        RejectOnExisting(builder, "WithDescription");
        builder.Resource.Description = description;
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
    /// Microsoft.Identity.Web names (<c>AzureAd__Instance</c>, <c>AzureAd__TenantId</c>, <c>AzureAd__ClientId</c>, plus
    /// <c>AzureAd__Scopes</c> for registrations that expose scopes, <c>ManagedIdentityClientId</c> for federated credentials and
    /// <c>DownstreamApis__{api}__Scopes__{n}</c> for APIs the registration has permissions on); other resources get an uppercase
    /// resource-name prefix with a single underscore separator and only the basic settings.
    /// </summary>
    public static IResourceBuilder<TDestination> WithReference<TDestination>(
        this IResourceBuilder<TDestination> builder, IResourceBuilder<EntraIdApplicationResource> source)
        where TDestination : IResourceWithEnvironment =>
        WithReference(builder, source, "AzureAd", "DownstreamApis");

    /// <summary>Like <see cref="WithReference{TDestination}(IResourceBuilder{TDestination}, IResourceBuilder{EntraIdApplicationResource})"/> with a custom connection (section) name and downstream APIs root key.</summary>
    public static IResourceBuilder<TDestination> WithReference<TDestination>(
        this IResourceBuilder<TDestination> builder, IResourceBuilder<EntraIdApplicationResource> source, string connectionName, string downstreamApisConfigKey = "DownstreamApis")
        where TDestination : IResourceWithEnvironment
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionName);
        ArgumentException.ThrowIfNullOrEmpty(downstreamApisConfigKey);
        var app = source.Resource;
        var isDotNet = builder.Resource is ProjectResource;
        var prefix = isDotNet ? connectionName : app.Name.ToUpperInvariant().Replace('-', '_');
        var sep = isDotNet ? "__" : "_";
        return builder.WithEnvironment(ctx =>
        {
            ctx.EnvironmentVariables[$"{prefix}{sep}Instance"] = app.Instance;
            ctx.EnvironmentVariables[$"{prefix}{sep}TenantId"] = app.TenantIdValue;
            ctx.EnvironmentVariables[$"{prefix}{sep}ClientId"] = app.ClientIdValue;
            if (!isDotNet) return;

            if (app.Scopes.Count > 0)
                ctx.EnvironmentVariables[$"{prefix}__Scopes"] = string.Join(' ', app.Scopes.Select(s => s.Name));
            if (app.FederatedManagedIdentityClientId is { } miClientId)
                ctx.EnvironmentVariables["ManagedIdentityClientId"] = miClientId;

            foreach (var req in app.ApiPermissions)
            {
                if (req.Modeled is not { } api) continue;
                var root = $"{downstreamApisConfigKey}__{api.Name}";
                var delegated = req.Permissions.Where(p => p.Type == EntraIdApiPermissionType.Delegated).ToList();
                for (var i = 0; i < delegated.Count; i++)
                    ctx.EnvironmentVariables[$"{root}__Scopes__{i}"] = api.GetScopeUri(delegated[i].Name);
                // App-only token requests use the API's .default scope.
                var appOnly = $"{downstreamApisConfigKey}__{api.Name}AppOnly";
                ctx.EnvironmentVariables[$"{appOnly}__Scopes__0"] = api.DefaultScopeUri;
                ctx.EnvironmentVariables[$"{appOnly}__RequestAppToken"] = "true";
            }
        });
    }

    private static void RejectOnExisting(IResourceBuilder<EntraIdApplicationResource> builder, string method)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Resource.IsExistingApplication)
            throw new InvalidOperationException($"{method} configures the registration itself, but '{builder.Resource.Name}' is an existing application that this AppHost does not provision. Configure it where it is managed.");
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
