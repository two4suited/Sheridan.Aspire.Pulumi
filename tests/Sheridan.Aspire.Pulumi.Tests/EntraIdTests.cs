using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure;
using Sheridan.Aspire.Pulumi.Azure.EntraId;
using Sheridan.Aspire.Pulumi.Provisioning;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

public sealed class EntraIdTests
{
    private static readonly string Tenant = Guid.NewGuid().ToString();
    private static readonly string Client = Guid.NewGuid().ToString();

    private static IDistributedApplicationBuilder NewBuilder() => DistributedApplication.CreateBuilder([]);

    private static PulumiProgram Build(IResourceBuilder<EntraIdApplicationResource> b)
    {
        var program = new PulumiProgram();
        b.Resource.Build(program, "proj", "dev");
        return program;
    }

    private static Dictionary<string, object?> Props(PulumiProgram p, string id) =>
        (Dictionary<string, object?>)((Dictionary<string, object?>)p.Resources[id]!)["properties"]!;

    private sealed class FakeIdentity(string name) : Resource(name), IPulumiManagedIdentityResource
    {
        public string PrincipalIdExpression => $"${{{Name}.principalId}}";
    }

    [Fact]
    public void Existing_application_rejects_registration_only_calls_in_both_orders()
    {
        var b = NewBuilder();
        var existing = b.AddPulumiExistingEntraApplication("e", Tenant, Client);
        Assert.Throws<InvalidOperationException>(() => existing.WithRedirectUris("https://x"));
        Assert.Throws<InvalidOperationException>(() => existing.WithOwners(Guid.NewGuid().ToString()));
        Assert.Throws<InvalidOperationException>(() => existing.WithIdTokenIssuance());
        Assert.Throws<InvalidOperationException>(() => existing.WithAppRole(new EntraIdAppRole("R", "R", "R", ["Application"])));
        Assert.Throws<InvalidOperationException>(() => existing.WithApplicationIdUri());
        Assert.Throws<InvalidOperationException>(() => existing.WithAdminConsent());
        Assert.Throws<InvalidOperationException>(() => existing.ConfigureInfrastructure((PulumiEntraIdInfrastructure _) => { }));
        existing.WithApplicationIdUri("api://custom"); // consumer metadata stays allowed

        var configured = b.AddPulumiEntraId("c").WithRedirectUris("https://x");
        Assert.Throws<InvalidOperationException>(() => configured.AsExistingApplication(Tenant, Client));
    }

    [Fact]
    public void Scope_and_role_ids_are_stable_and_distinct()
    {
        var b = NewBuilder();
        var a1 = b.AddPulumiEntraId("api").WithScope("Orders.Read", "Read").WithAppRole(new EntraIdAppRole("Orders.Read", "Read", "Read", ["Application"]));
        var a2 = NewBuilder().AddPulumiEntraId("api").WithScope("Orders.Read", "Read");
        Assert.Equal(a1.Resource.GetScopeId("Orders.Read"), a2.Resource.GetScopeId("Orders.Read"));
        Assert.NotEqual(a1.Resource.GetScopeId("Orders.Read"), a1.Resource.GetRoleId("Orders.Read"));
        var explicitId = Guid.NewGuid();
        var a3 = b.AddPulumiEntraId("api3").WithScope("S", "d", s => s.Id = explicitId);
        Assert.Equal(explicitId, a3.Resource.GetScopeId("S"));
    }

    [Fact]
    public void Scopes_roles_and_default_identifier_uri_are_emitted()
    {
        var b = NewBuilder();
        var api = b.AddPulumiEntraId("api").WithApplicationIdUri().WithScope("Orders.Read", "Read orders")
            .WithAppRole(new EntraIdAppRole("Orders.Write", "Write", "Write orders", ["Application"]) { IsEnabled = false })
            .WithTags("a").WithDescription("d");
        var p = Build(api);
        var app = Props(p, "apiApp");
        var scope = (Dictionary<string, object?>)((List<object?>)((Dictionary<string, object?>)app["api"]!)["oauth2PermissionScopes"]!)[0]!;
        Assert.Equal("Orders.Read", scope["value"]);
        Assert.Equal("User", scope["type"]);
        var role = (Dictionary<string, object?>)((List<object?>)app["appRoles"]!)[0]!;
        Assert.Equal(false, role["enabled"]);
        Assert.Equal("d", app["description"]);
        Assert.False(app.ContainsKey("identifierUris"));
        Assert.Equal("azuread:index/applicationIdentifierUri:ApplicationIdentifierUri", ((Dictionary<string, object?>)p.Resources["apiIdentifierUri"]!)["type"]);
        Assert.Contains("ignoreChanges", (Dictionary<string, object?>)((Dictionary<string, object?>)p.Resources["apiApp"]!)["options"]!);
    }

    [Fact]
    public void Explicit_identifier_uri_is_set_on_the_application()
    {
        var p = Build(NewBuilder().AddPulumiEntraId("api").WithApplicationIdUri("api://orders"));
        Assert.Equal("api://orders", ((List<object?>)Props(p, "apiApp")["identifierUris"]!)[0]);
        Assert.False(p.Resources.ContainsKey("apiIdentifierUri"));
    }

    [Fact]
    public void Permissions_pre_authorization_and_consent_are_emitted()
    {
        var b = NewBuilder();
        var api = b.AddPulumiEntraId("api").WithScope("Orders.Read", "Read");
        var web = b.AddPulumiEntraId("web")
            .WithApiPermissions(api, "Orders.Read")
            .WithApiPermissions(EntraIdApi.MicrosoftGraph, EntraIdApiPermission.Delegated("User.Read"), EntraIdApiPermission.Application("Mail.Send"))
            .WithAdminConsent();
        api.WithPreAuthorizedApplication(web, "Orders.Read");

        var wp = Build(web);
        var access = (List<object?>)Props(wp, "webApp")["requiredResourceAccesses"]!;
        Assert.Equal(2, access.Count);
        var graph = (Dictionary<string, object?>)access[1]!;
        Assert.Equal("00000003-0000-0000-c000-000000000000", graph["resourceAppId"]);
        var accesses = ((List<object?>)graph["resourceAccesses"]!).Cast<Dictionary<string, object?>>().ToList();
        Assert.Equal("Scope", accesses[0]["type"]);
        Assert.Contains("oauth2PermissionScopeIds", (string)accesses[0]["id"]!);
        Assert.Equal("Role", accesses[1]["type"]);
        Assert.Contains("azuread:index/servicePrincipalDelegatedPermissionGrant:ServicePrincipalDelegatedPermissionGrant", wp.Resources.Values.Select(v => (string)((Dictionary<string, object?>)v!)["type"]!));
        Assert.Contains("azuread:index/appRoleAssignment:AppRoleAssignment", wp.Resources.Values.Select(v => (string)((Dictionary<string, object?>)v!)["type"]!));

        var ap = Build(api);
        var pre = Props(ap, "apiPreAuthweb");
        Assert.Equal("${webApp.clientId}", pre["authorizedClientId"]);
        Assert.Single((List<object?>)pre["permissionIds"]!);
    }

    [Fact]
    public void Declaring_permissions_without_consent_grants_nothing()
    {
        var p = Build(NewBuilder().AddPulumiEntraId("web").WithApiPermissions(EntraIdApi.MicrosoftGraph, EntraIdApiPermission.Delegated("User.Read")));
        Assert.DoesNotContain(p.Resources.Keys, k => k.Contains("Consent") || k.Contains("RoleGrant"));
    }

    [Fact]
    public void Federated_credential_and_role_assignment_trust_the_identity()
    {
        var b = NewBuilder();
        var id = b.AddResource(new FakeIdentity("mi"));
        var api = b.AddPulumiEntraId("api")
            .WithAppRole(new EntraIdAppRole("Orders.Read", "Read", "Read", ["Application"]))
            .PublishWithFederatedManagedIdentityCredential(id)
            .PublishWithAppRoleAssignment(id, "Orders.Read");
        var p = Build(api);
        var fic = Props(p, "apiFic0");
        Assert.Equal("${mi.principalId}", fic["subject"]);
        Assert.Equal("api://AzureADTokenExchange", ((List<object?>)fic["audiences"]!)[0]);
        Assert.Equal("${mi.principalId}", Props(p, "apiRoleAssignment0")["principalObjectId"]);
        Assert.Throws<InvalidOperationException>(() => api.PublishWithAppRoleAssignment(id, "Nope"));
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com/", "api://AzureADTokenExchange")]
    [InlineData("https://login.microsoftonline.us/", "api://AzureADTokenExchangeUSGov")]
    [InlineData("https://login.chinacloudapi.cn/", "api://AzureADTokenExchangeChina")]
    public void Token_exchange_audience_follows_the_cloud(string instance, string expected) =>
        Assert.Equal(expected, EntraIdApplicationResource.TokenExchangeAudience(instance));

    [Fact]
    public async Task Overlay_can_edit_and_new_properties_are_emitted()
    {
        var b = NewBuilder();
        var app = b.AddPulumiEntraId("app").WithTags("t1").WithServiceManagementReference("ref")
            .ConfigureInfrastructure(i => i.Application.Set("notes", "n"));
        var p = Build(app);
        app.Resource.ApplyInfrastructureCallbacks(p);
        var props = Props(p, "appApp");
        Assert.Equal("ref", props["serviceManagementReference"]);
        Assert.Equal("n", props["notes"]);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task WithReference_injects_downstream_apis_scopes_and_managed_identity_for_dotnet_projects()
    {
        var b = NewBuilder();
        var stack = b.AddPulumiStack("s", "dev");
        var api = b.AddPulumiEntraId("orders-api").WithPulumiStack(stack).WithApplicationIdUri("api://orders").WithScope("Orders.Read", "Read").WithScope("Orders.Write", "Write");
        var web = b.AddPulumiEntraId("web").WithPulumiStack(stack).WithApiPermissions(api, "Orders.Read").WithFederatedManagedIdentityCredential(Client);

        var webProject = b.AddResource(new ProjectResource("webapp"));
        var env = await EnvironmentVariablesAsync(webProject, x => x.WithReference(web));
        Assert.Equal("api://orders/Orders.Read", env["DownstreamApis__orders-api__Scopes__0"]);
        Assert.Equal("api://orders/.default", env["DownstreamApis__orders-apiAppOnly__Scopes__0"]);
        Assert.Equal("true", env["DownstreamApis__orders-apiAppOnly__RequestAppToken"]);
        Assert.Equal(Client, env["ManagedIdentityClientId"]);

        var apiProject = b.AddResource(new ProjectResource("apiapp"));
        var apiEnv = await EnvironmentVariablesAsync(apiProject, x => x.WithReference(api));
        Assert.Equal("Orders.Read Orders.Write", apiEnv["AzureAd__Scopes"]);

        var custom = await EnvironmentVariablesAsync(b.AddResource(new ProjectResource("p3")), x => x.WithReference(web, "Entra", "Apis"));
        Assert.Contains("Entra__ClientId", custom.Keys);
        Assert.Contains("Apis__orders-api__Scopes__0", custom.Keys);
    }

    [Fact]
    public async Task WithReference_on_non_dotnet_resources_only_injects_prefixed_basics()
    {
        var b = NewBuilder();
        var web = b.AddPulumiEntraId("web").WithPulumiStack(b.AddPulumiStack("s", "dev")).WithScope("S", "d");
        var env = await EnvironmentVariablesAsync(b.AddContainer("d", "img"), x => x.WithReference(web));
        Assert.Contains("WEB_ClientId", env.Keys);
        Assert.DoesNotContain(env.Keys, k => k.Contains("Scopes", StringComparison.Ordinal));
    }

    private static async Task<Dictionary<string, object>> EnvironmentVariablesAsync<T>(IResourceBuilder<T> rb, Func<IResourceBuilder<T>, IResourceBuilder<T>> configure)
        where T : IResourceWithEnvironment
    {
        configure(rb);
        var env = new Dictionary<string, object>();
        var ctx = new EnvironmentCallbackContext(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish), env);
        foreach (var a in rb.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>()) await a.Callback(ctx);
        return env;
    }
}
