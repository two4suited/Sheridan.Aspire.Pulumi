using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using AspirePulumiPackages.Pulumi;

namespace AspirePulumiPackages.Pulumi.Azure.EntraId;

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

    public EntraIdSignInAudience SignInAudience { get; set; } = EntraIdSignInAudience.AzureADMyOrg;

    public bool EnableIdTokenIssuance { get; set; }

    public List<string> RedirectUris { get; } = [];
    public List<string> ApplicationOwners { get; } = [];
    public List<string> ServicePrincipalOwners { get; } = [];

    /// <summary>True when referencing a registration that already exists; nothing is provisioned.</summary>
    public bool IsExistingApplication { get; internal set; }
    public string? ExistingTenantId { get; internal set; }
    public string? ExistingClientId { get; internal set; }

    private string Id => string.Concat(
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

    public Task ContributeAsync(PulumiProgram program, PipelineStepContext ctx)
    {
        if (IsExistingApplication)
        {
            return Task.CompletedTask;
        }

        var web = new Dictionary<string, object?>
        {
            ["implicitGrant"] = new Dictionary<string, object?> { ["idTokenIssuanceEnabled"] = EnableIdTokenIssuance },
        };
        if (RedirectUris.Count > 0)
        {
            web["redirectUris"] = RedirectUris.Cast<object?>().ToList();
        }

        var appProps = new Dictionary<string, object?>
        {
            ["displayName"] = Name,
            ["signInAudience"] = SignInAudience.ToString(),
            ["api"] = new Dictionary<string, object?> { ["requestedAccessTokenVersion"] = 2 },
            ["web"] = web,
        };
        if (ApplicationOwners.Count > 0)
        {
            appProps["owners"] = ApplicationOwners.Cast<object?>().ToList();
        }

        program.Variables[$"{Id}ClientConfig"] = new Dictionary<string, object?>
        {
            ["fn::invoke"] = new Dictionary<string, object?>
            {
                ["function"] = "azuread:index/getClientConfig:getClientConfig",
                ["arguments"] = new Dictionary<string, object?>(),
            },
        };

        program.Resources[$"{Id}App"] = new Dictionary<string, object?>
        {
            ["type"] = "azuread:index/application:Application",
            ["properties"] = appProps,
        };

        var spProps = new Dictionary<string, object?> { ["clientId"] = $"${{{Id}App.clientId}}" };
        if (ServicePrincipalOwners.Count > 0)
        {
            spProps["owners"] = ServicePrincipalOwners.Cast<object?>().ToList();
        }

        program.Resources[$"{Id}Sp"] = new Dictionary<string, object?>
        {
            ["type"] = "azuread:index/servicePrincipal:ServicePrincipal",
            ["properties"] = spProps,
        };

        program.Outputs[$"{Id}ClientId"] = $"${{{Id}App.clientId}}";
        program.Outputs[$"{Id}TenantId"] = $"${{{Id}ClientConfig.tenantId}}";
        program.Outputs[$"{Id}ObjectId"] = $"${{{Id}App.objectId}}";
        program.Outputs[$"{Id}ServicePrincipalObjectId"] = $"${{{Id}Sp.objectId}}";

        return Task.CompletedTask;
    }
}
