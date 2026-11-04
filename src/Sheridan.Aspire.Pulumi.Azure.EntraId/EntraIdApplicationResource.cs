using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Sheridan.Aspire.Pulumi;
using Sheridan.Aspire.Pulumi.Azure;
using Sheridan.Aspire.Pulumi.Provisioning;

namespace Sheridan.Aspire.Pulumi.Azure.EntraId;

/// <summary>
/// A Microsoft Entra ID application registration and service principal provisioned by Pulumi (azuread provider).
/// Pulumi counterpart of the official <c>EntraIdApplicationResource</c>.
/// </summary>
public sealed class EntraIdApplicationResource(string name) : Resource(name), IPulumiProgramContributor
{
    internal const string DefaultInstance = "https://login.microsoftonline.com/";

    public PulumiStackResource? Stack { get; set; }

    /// <summary>The Entra instance (authority host) used by consumers. Defaults to the public cloud.</summary>
    public string Instance { get; set; } = DefaultInstance;

    private string? _displayName;

    /// <summary>Display name of the app registration (1-256 characters); defaults to <c>{app}-{resource}-{stack}</c> (see <see cref="PulumiEntraNaming"/>).</summary>
    public string? DisplayName { get => _displayName; set => _displayName = value is null ? null : PulumiEntraNaming.ValidateDisplayName(value, nameof(DisplayName)); }

    public EntraIdSignInAudience SignInAudience { get; set; } = EntraIdSignInAudience.AzureADMyOrg;

    public bool EnableIdTokenIssuance { get; set; }

    public List<string> RedirectUris { get; } = [];
    public List<string> ApplicationOwners { get; } = [];
    public List<string> ServicePrincipalOwners { get; } = [];

    // --- API (resource server) side ---
    /// <summary>True when <c>api://{clientId}</c> should be registered as the identifier URI after the application exists.</summary>
    public bool UseDefaultApplicationIdUri { get; internal set; }
    /// <summary>An explicit identifier URI, set on the application directly.</summary>
    public string? ApplicationIdUri { get; internal set; }
    public List<EntraIdScope> Scopes { get; } = [];
    public List<EntraIdAppRole> AppRoles { get; } = [];
    internal List<(EntraIdApplicationResource Client, string[] Scopes)> PreAuthorizedApplications { get; } = [];

    // --- Caller side ---
    internal List<ApiPermissionRequest> ApiPermissions { get; } = [];
    public bool AdminConsent { get; internal set; }

    // --- Identity / credentials ---
    internal List<IPulumiManagedIdentityResource> FederatedIdentities { get; } = [];
    /// <summary>Managed identities granted one of this registration's app roles.</summary>
    internal List<(string Role, IPulumiManagedIdentityResource Identity)> AppRoleAssignments { get; } = [];
    /// <summary>Client ID of a managed identity trusted via a federated credential created outside this program (config only).</summary>
    public string? FederatedManagedIdentityClientId { get; internal set; }
    /// <summary>Name of the root configuration section for downstream APIs.</summary>
    public string DownstreamApisConfigKey { get; internal set; } = "DownstreamApis";

    // --- Extra application / service principal properties (the public-model additions of upstream #21724) ---
    public List<string> Tags { get; } = [];
    public string? ServiceManagementReference { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public string? GroupMembershipClaims { get; set; }
    public bool? IsFallbackPublicClient { get; set; }
    public string? HomePageUrl { get; set; }
    public string? LogoutUrl { get; set; }
    public List<string> SpaRedirectUris { get; } = [];
    public List<string> PublicClientRedirectUris { get; } = [];
    public bool? ServicePrincipalAccountEnabled { get; set; }
    public bool? ServicePrincipalAppRoleAssignmentRequired { get; set; }
    public List<string> ServicePrincipalNotificationEmailAddresses { get; } = [];

    /// <summary>True when referencing a registration that already exists; nothing is provisioned.</summary>
    public bool IsExistingApplication { get; internal set; }
    public string? ExistingTenantId { get; internal set; }
    public string? ExistingClientId { get; internal set; }

    internal string Id => string.Concat(
        Name.Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select((p, i) => i == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));

    /// <summary>Client ID: a Pulumi stack output, or the fixed value for an existing application.</summary>
    public object ClientIdValue => IsExistingApplication ? ExistingClientId! : ClientId;

    /// <summary>Tenant ID: a Pulumi stack output, or the fixed value for an existing application.</summary>
    public object TenantIdValue => IsExistingApplication ? ExistingTenantId! : TenantId;

    public PulumiOutputReference ClientId => Output("ClientId");
    public PulumiOutputReference TenantId => Output("TenantId");
    public PulumiOutputReference ObjectId => Output("ObjectId");
    public PulumiOutputReference ServicePrincipalObjectId => Output("ServicePrincipalObjectId");

    private PulumiOutputReference Output(string suffix) =>
        new(Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet."), $"{Id}{suffix}");

    /// <summary>The first setting that only applies to a provisioned registration, or null. Used to reject converting a configured resource to an existing one.</summary>
    internal string? FirstRegistrationOnlySetting() =>
        RedirectUris.Count > 0 ? "redirect URIs"
        : ApplicationOwners.Count > 0 || ServicePrincipalOwners.Count > 0 ? "owners"
        : EnableIdTokenIssuance ? "ID token issuance"
        : UseDefaultApplicationIdUri ? "an application ID URI"
        : AppRoles.Count > 0 ? "app roles"
        : PreAuthorizedApplications.Count > 0 ? "pre-authorized applications"
        : ApiPermissions.Count > 0 ? "API permissions"
        : AdminConsent ? "admin consent"
        : FederatedIdentities.Count > 0 ? "a federated credential"
        : AppRoleAssignments.Count > 0 ? "app role assignments"
        : Tags.Count > 0 || ServiceManagementReference is not null || Description is not null ? "application metadata"
        : null;

    /// <summary>Scope ID: the explicit one, or a stable GUID derived from the registration name and scope name.</summary>
    public Guid GetScopeId(string scopeName)
    {
        var scope = Scopes.FirstOrDefault(s => s.Name == scopeName)
            ?? throw new InvalidOperationException($"'{Name}' does not declare a scope named '{scopeName}'. Add it with WithScope first.");
        return scope.Id ?? EntraIdIds.Stable("scope", Name, scope.Name);
    }

    internal static Guid GetRoleId(string registrationName, EntraIdAppRole role) =>
        role.Id ?? EntraIdIds.Stable("role", registrationName, role.Value);

    internal Guid GetRoleId(string roleValue)
    {
        var role = AppRoles.FirstOrDefault(r => r.Value == roleValue)
            ?? throw new InvalidOperationException($"'{Name}' does not declare an app role '{roleValue}'. Add it with WithAppRole first.");
        return GetRoleId(Name, role);
    }

    /// <summary>The identifier URI of this registration as a value usable in configuration (<c>api://{clientId}</c> by default).</summary>
    internal object ApplicationIdUriValue =>
        ApplicationIdUri is { } u ? u : DefaultUri(null);

    private ReferenceExpression DefaultUri(string? suffix) => IsExistingApplication
        ? ReferenceExpression.Create($"api://{ExistingClientId}{suffix}")
        : ReferenceExpression.Create($"api://{ClientId}{suffix}");

    /// <summary>The <c>.default</c> scope URI used for app-only token requests.</summary>
    internal object DefaultScopeUri => ApplicationIdUri is { } u ? $"{u.TrimEnd('/')}/.default" : DefaultUri("/.default");

    /// <summary>The full scope URI (<c>{identifierUri}/{scope}</c>).</summary>
    public object GetScopeUri(string scopeName)
    {
        _ = GetScopeId(scopeName);
        return ApplicationIdUri is { } u ? $"{u.TrimEnd('/')}/{scopeName}" : DefaultUri($"/{scopeName}");
    }

    /// <summary>Pulumi expression for the client ID, only valid inside the program that creates this registration.</summary>
    internal string ClientIdExpression => IsExistingApplication ? ExistingClientId! : $"${{{Id}App.clientId}}";

    /// <summary>Pulumi expression for the service principal object ID, or a lookup variable for existing applications.</summary>
    internal string ServicePrincipalObjectIdExpression => $"${{{Id}Sp.objectId}}";

    public Task ContributeAsync(PulumiProgram program, PipelineStepContext ctx)
    {
        if (IsExistingApplication)
        {
            return Task.CompletedTask;
        }

        var stack = Stack ?? ctx.Model.Resources.OfType<PulumiStackResource>().SingleOrDefault();
        Build(program, stack?.ProjectName, stack?.StackName);
        this.ApplyInfrastructureCallbacks(program);
        return Task.CompletedTask;
    }

    internal void Build(PulumiProgram program, string? projectName, string? stackName)
    {
        var displayName = DisplayName ?? PulumiEntraNaming.DisplayName(projectName, Name, stackName);

        var web = new Dictionary<string, object?>
        {
            ["implicitGrant"] = new Dictionary<string, object?> { ["idTokenIssuanceEnabled"] = EnableIdTokenIssuance },
        };
        if (RedirectUris.Count > 0) web["redirectUris"] = Strings(RedirectUris);
        if (HomePageUrl is not null) web["homepageUrl"] = HomePageUrl;
        if (LogoutUrl is not null) web["logoutUrl"] = LogoutUrl;

        var api = new Dictionary<string, object?> { ["requestedAccessTokenVersion"] = 2 };
        if (Scopes.Count > 0)
        {
            api["oauth2PermissionScopes"] = Scopes.Select(sc => (object?)new Dictionary<string, object?>
            {
                ["id"] = (sc.Id ?? EntraIdIds.Stable("scope", Name, sc.Name)).ToString(),
                ["value"] = sc.Name,
                ["enabled"] = sc.IsEnabled,
                ["type"] = sc.RequiresAdminConsent ? "Admin" : "User",
                ["adminConsentDisplayName"] = sc.AdminConsentDisplayName ?? sc.Name,
                ["adminConsentDescription"] = sc.Description,
                ["userConsentDisplayName"] = sc.UserConsentDisplayName ?? sc.AdminConsentDisplayName ?? sc.Name,
                ["userConsentDescription"] = sc.UserConsentDescription ?? sc.Description,
            }).ToList();
        }

        var appProps = new Dictionary<string, object?>
        {
            ["displayName"] = displayName,
            ["signInAudience"] = SignInAudience.ToString(),
            ["api"] = api,
            ["web"] = web,
        };
        if (ApplicationOwners.Count > 0) appProps["owners"] = Strings(ApplicationOwners);
        if (ApplicationIdUri is not null) appProps["identifierUris"] = new List<object?> { ApplicationIdUri };
        if (AppRoles.Count > 0)
        {
            appProps["appRoles"] = AppRoles.Select(r => (object?)new Dictionary<string, object?>
            {
                ["id"] = GetRoleId(Name, r).ToString(),
                ["value"] = r.Value,
                ["displayName"] = r.DisplayName,
                ["description"] = r.Description,
                ["allowedMemberTypes"] = Strings(r.AllowedMemberTypes),
                ["enabled"] = r.IsEnabled,
            }).ToList();
        }
        if (Tags.Count > 0) appProps["tags"] = Strings(Tags);
        if (ServiceManagementReference is not null) appProps["serviceManagementReference"] = ServiceManagementReference;
        if (Description is not null) appProps["description"] = Description;
        if (Notes is not null) appProps["notes"] = Notes;
        if (GroupMembershipClaims is not null) appProps["groupMembershipClaims"] = new List<object?> { GroupMembershipClaims };
        if (IsFallbackPublicClient is { } fb) appProps["fallbackPublicClientEnabled"] = fb;
        if (SpaRedirectUris.Count > 0) appProps["singlePageApplication"] = new Dictionary<string, object?> { ["redirectUris"] = Strings(SpaRedirectUris) };
        if (PublicClientRedirectUris.Count > 0) appProps["publicClient"] = new Dictionary<string, object?> { ["redirectUris"] = Strings(PublicClientRedirectUris) };

        var permissions = BuildRequiredResourceAccess(program);
        if (permissions.Count > 0) appProps["requiredResourceAccesses"] = permissions;

        program.Variables[$"{Id}ClientConfig"] = Invoke("azuread:index/getClientConfig:getClientConfig", new Dictionary<string, object?>());

        var appDef = new Dictionary<string, object?>
        {
            ["type"] = "azuread:index/application:Application",
            ["properties"] = appProps,
        };
        // The default identifier URI embeds the client ID, which only exists after the application does, so it is set by a
        // separate resource; the application must then ignore drift on identifierUris.
        if (UseDefaultApplicationIdUri && ApplicationIdUri is null)
        {
            appDef["options"] = new Dictionary<string, object?> { ["ignoreChanges"] = new List<object?> { "identifierUris" } };
        }
        program.Resources[$"{Id}App"] = appDef;

        if (UseDefaultApplicationIdUri && ApplicationIdUri is null)
        {
            program.Resources[$"{Id}IdentifierUri"] = new Dictionary<string, object?>
            {
                ["type"] = "azuread:index/applicationIdentifierUri:ApplicationIdentifierUri",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["applicationId"] = $"${{{Id}App.id}}",
                    ["identifierUri"] = $"api://${{{Id}App.clientId}}",
                },
            };
        }

        var spProps = new Dictionary<string, object?> { ["clientId"] = $"${{{Id}App.clientId}}" };
        if (ServicePrincipalOwners.Count > 0) spProps["owners"] = Strings(ServicePrincipalOwners);
        if (ServicePrincipalAccountEnabled is { } ae) spProps["accountEnabled"] = ae;
        if (ServicePrincipalAppRoleAssignmentRequired is { } ar) spProps["appRoleAssignmentRequired"] = ar;
        if (ServicePrincipalNotificationEmailAddresses.Count > 0) spProps["notificationEmailAddresses"] = Strings(ServicePrincipalNotificationEmailAddresses);

        program.Resources[$"{Id}Sp"] = new Dictionary<string, object?>
        {
            ["type"] = "azuread:index/servicePrincipal:ServicePrincipal",
            ["properties"] = spProps,
        };

        AddPreAuthorizations(program);
        AddConsent(program);
        AddFederatedCredentials(program);
        AddAppRoleAssignments(program);

        program.Outputs[$"{Id}ClientId"] = $"${{{Id}App.clientId}}";
        program.Outputs[$"{Id}TenantId"] = $"${{{Id}ClientConfig.tenantId}}";
        program.Outputs[$"{Id}ObjectId"] = $"${{{Id}App.objectId}}";
        program.Outputs[$"{Id}ServicePrincipalObjectId"] = $"${{{Id}Sp.objectId}}";
    }

    private static List<object?> Strings(IEnumerable<string> v) => v.Cast<object?>().ToList();

    private static Dictionary<string, object?> Invoke(string function, Dictionary<string, object?> args) => new()
    {
        ["fn::invoke"] = new Dictionary<string, object?> { ["function"] = function, ["arguments"] = args },
    };

    /// <summary>Declares one lookup variable per well-known API (permission name to GUID maps and the service principal object ID).</summary>
    private string LookupVariable(PulumiProgram program, EntraIdApi api)
    {
        var name = $"{Id}Lookup{api.Name}";
        program.Variables[name] = Invoke("azuread:index/getServicePrincipal:getServicePrincipal",
            new Dictionary<string, object?> { ["clientId"] = api.ApplicationId });
        return name;
    }

    private string PermissionId(string lookupVar, EntraIdApiPermission p) => p.Type == EntraIdApiPermissionType.Delegated
        ? $"${{{lookupVar}.oauth2PermissionScopeIds[\"{p.Name}\"]}}"
        : $"${{{lookupVar}.appRoleIds[\"{p.Name}\"]}}";

    private List<object?> BuildRequiredResourceAccess(PulumiProgram program)
    {
        var result = new List<object?>();
        foreach (var req in ApiPermissions)
        {
            var access = new List<object?>();
            if (req.Modeled is { } modeled)
            {
                foreach (var p in req.Permissions)
                {
                    var id = p.Type == EntraIdApiPermissionType.Delegated ? modeled.GetScopeId(p.Name) : modeled.GetRoleId(p.Name);
                    access.Add(new Dictionary<string, object?> { ["id"] = id.ToString(), ["type"] = p.Type == EntraIdApiPermissionType.Delegated ? "Scope" : "Role" });
                }
                result.Add(new Dictionary<string, object?> { ["resourceAppId"] = modeled.ClientIdExpression, ["resourceAccesses"] = access });
            }
            else
            {
                var lookup = LookupVariable(program, req.WellKnown!);
                foreach (var p in req.Permissions)
                {
                    access.Add(new Dictionary<string, object?> { ["id"] = PermissionId(lookup, p), ["type"] = p.Type == EntraIdApiPermissionType.Delegated ? "Scope" : "Role" });
                }
                result.Add(new Dictionary<string, object?> { ["resourceAppId"] = req.WellKnown!.ApplicationId, ["resourceAccesses"] = access });
            }
        }
        return result;
    }

    private void AddPreAuthorizations(PulumiProgram program)
    {
        // Declared on the API (this resource); each client gets one ApplicationPreAuthorized resource. The client is only read by
        // client ID, so a pair that authorizes each other is not a cycle in the Pulumi graph.
        foreach (var (client, scopes) in PreAuthorizedApplications)
        {
            program.Resources[$"{Id}PreAuth{client.Id}"] = new Dictionary<string, object?>
            {
                ["type"] = "azuread:index/applicationPreAuthorized:ApplicationPreAuthorized",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["applicationId"] = $"${{{Id}App.id}}",
                    ["authorizedClientId"] = client.ClientIdExpression,
                    ["permissionIds"] = scopes.Select(sc => (object?)GetScopeId(sc).ToString()).ToList(),
                },
            };
        }
    }

    private void AddConsent(PulumiProgram program)
    {
        if (!AdminConsent) return;
        var n = 0;
        foreach (var req in ApiPermissions)
        {
            if (req.Modeled is { IsExistingApplication: true } existing)
                throw new InvalidOperationException($"Admin consent for permissions on '{existing.Name}' needs its service principal, which is not provisioned for an existing application.");
            var resourceSp = req.Modeled is { } m ? m.ServicePrincipalObjectIdExpression : $"${{{LookupVariable(program, req.WellKnown!)}.objectId}}";
            var delegated = req.Permissions.Where(p => p.Type == EntraIdApiPermissionType.Delegated).Select(p => p.Name).ToList();
            if (delegated.Count > 0)
            {
                program.Resources[$"{Id}Consent{n}"] = new Dictionary<string, object?>
                {
                    ["type"] = "azuread:index/servicePrincipalDelegatedPermissionGrant:ServicePrincipalDelegatedPermissionGrant",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["servicePrincipalObjectId"] = $"${{{Id}Sp.objectId}}",
                        ["resourceServicePrincipalObjectId"] = resourceSp,
                        ["claimValues"] = Strings(delegated),
                    },
                };
            }
            var lookup = req.Modeled is null ? LookupVariable(program, req.WellKnown!) : null;
            var roleIndex = 0;
            foreach (var role in req.Permissions.Where(p => p.Type == EntraIdApiPermissionType.Application))
            {
                program.Resources[$"{Id}RoleGrant{n}x{roleIndex++}"] = new Dictionary<string, object?>
                {
                    ["type"] = "azuread:index/appRoleAssignment:AppRoleAssignment",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["appRoleId"] = req.Modeled is { } mm ? mm.GetRoleId(role.Name).ToString() : PermissionId(lookup!, role),
                        ["principalObjectId"] = $"${{{Id}Sp.objectId}}",
                        ["resourceObjectId"] = resourceSp,
                    },
                };
            }
            n++;
        }
    }

    private void AddFederatedCredentials(PulumiProgram program)
    {
        var n = 0;
        foreach (var identity in FederatedIdentities.Distinct())
        {
            program.Resources[$"{Id}Fic{n++}"] = new Dictionary<string, object?>
            {
                ["type"] = "azuread:index/applicationFederatedIdentityCredential:ApplicationFederatedIdentityCredential",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["applicationId"] = $"${{{Id}App.id}}",
                    ["displayName"] = $"{Name}-{identity.Name}-msi",
                    ["description"] = $"Trusts the managed identity '{identity.Name}'.",
                    ["issuer"] = $"{Instance.TrimEnd('/')}/${{{Id}ClientConfig.tenantId}}/v2.0",
                    ["subject"] = identity.PrincipalIdExpression,
                    ["audiences"] = new List<object?> { TokenExchangeAudience(Instance) },
                },
            };
        }
    }

    /// <summary>The token exchange audience of the cloud the instance belongs to.</summary>
    internal static string TokenExchangeAudience(string instance) => new Uri(instance).Host switch
    {
        "login.microsoftonline.us" => "api://AzureADTokenExchangeUSGov",
        "login.chinacloudapi.cn" => "api://AzureADTokenExchangeChina",
        _ => "api://AzureADTokenExchange",
    };

    private void AddAppRoleAssignments(PulumiProgram program)
    {
        var n = 0;
        foreach (var (role, identity) in AppRoleAssignments.Distinct())
        {
            program.Resources[$"{Id}RoleAssignment{n++}"] = new Dictionary<string, object?>
            {
                ["type"] = "azuread:index/appRoleAssignment:AppRoleAssignment",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["appRoleId"] = GetRoleId(role).ToString(),
                    ["principalObjectId"] = identity.PrincipalIdExpression,
                    ["resourceObjectId"] = $"${{{Id}Sp.objectId}}",
                },
            };
        }
    }
}

/// <summary>A request for permissions on one API, either an AppHost-modeled registration or a well-known API.</summary>
internal sealed class ApiPermissionRequest
{
    public EntraIdApplicationResource? Modeled { get; init; }
    public EntraIdApi? WellKnown { get; init; }
    public List<EntraIdApiPermission> Permissions { get; } = [];
}
