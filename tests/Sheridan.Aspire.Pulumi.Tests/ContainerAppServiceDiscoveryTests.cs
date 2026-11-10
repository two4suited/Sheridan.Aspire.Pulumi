using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging.Abstractions;
using Sheridan.Aspire.Pulumi.Azure.AppContainers;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

/// <summary>
/// Service discovery between container apps of one environment (issue #201): endpoint references have no allocated address at
/// publish time, so they are mapped to the target app's ingress address as the official Container Apps publisher does.
/// </summary>
public sealed class ContainerAppServiceDiscoveryTests
{
    private sealed class CatalogProject : IProjectMetadata
    {
        public string ProjectPath => Path.Combine(Path.GetTempPath(), "sheridan-tests-no-such-dir", "Catalog.Api.csproj");

        // Empty launch settings keep AddProject from reading the (non-existent) project file.
        public LaunchSettings? LaunchSettings => new();
    }

    private sealed class WebProject : IProjectMetadata
    {
        public string ProjectPath => Path.Combine(Path.GetTempPath(), "sheridan-tests-no-such-dir", "Web.csproj");

        public LaunchSettings? LaunchSettings => new();
    }

    private static IDistributedApplicationBuilder PublishBuilder()
    {
        var builder = DistributedApplication.CreateBuilder(["--publisher", "manifest", "--output-path", Path.GetTempPath()]);
        Assert.True(builder.ExecutionContext.IsPublishMode);
        return builder;
    }

    /// <summary>What the environment's prepare step does for every compute resource it deploys.</summary>
    private static IResourceBuilder<T> Target<T>(IResourceBuilder<T> resource, IResourceBuilder<AzureContainerAppEnvironmentResource> env)
        where T : IResource
    {
        resource.Resource.Annotations.Add(new DeploymentTargetAnnotation(new PulumiContainerAppResource(resource.Resource.Name, resource.Resource))
        {
            ComputeEnvironment = env.Resource,
        });
        return resource;
    }

    private static async Task<Dictionary<string, string?>> EnvAsync(
        IDistributedApplicationBuilder builder, IResourceBuilder<AzureContainerAppEnvironmentResource> env, IResource resource)
    {
        var entries = await env.Resource.BuildEnvironmentAsync(resource, builder.ExecutionContext, NullLogger.Instance, stack: null, CancellationToken.None);
        return entries.Cast<Dictionary<string, object?>>().ToDictionary(e => (string)e["name"]!, e => (string?)e["value"]);
    }

    private static string Domain(IResourceBuilder<AzureContainerAppEnvironmentResource> env) => $"${{{env.Resource.Id}.defaultDomain}}";

    [Fact]
    public async Task Project_reference_to_an_internal_app_sets_http_and_https_service_discovery_variables()
    {
        var builder = PublishBuilder();
        var env = builder.AddPulumiAzureContainerAppEnvironment("aca-env");
        var catalog = Target(builder.AddProject<CatalogProject>("catalog-api").WithHttpEndpoint().WithHttpsEndpoint(), env);
        var web = Target(builder.AddProject<WebProject>("web").WithReference(catalog), env);

        var vars = await EnvAsync(builder, env, web.Resource);

        // Internal ingress: <app>.internal.<default domain>; the ingress serves HTTPS only, so http is upgraded as upstream does.
        var expected = $"https://catalog-api.internal.{Domain(env)}";
        Assert.Equal(expected, vars["services__catalog-api__http__0"]);
        Assert.Equal(expected, vars["services__catalog-api__https__0"]);
    }

    [Fact]
    public async Task Project_reference_to_an_external_app_uses_the_external_fqdn()
    {
        var builder = PublishBuilder();
        var env = builder.AddPulumiAzureContainerAppEnvironment("aca-env");
        var catalog = Target(builder.AddProject<CatalogProject>("catalog-api").WithHttpEndpoint().WithHttpsEndpoint().WithExternalHttpEndpoints(), env);
        var web = Target(builder.AddProject<WebProject>("web").WithReference(catalog), env);

        var vars = await EnvAsync(builder, env, web.Resource);

        var expected = $"https://catalog-api.{Domain(env)}";
        Assert.Equal(expected, vars["services__catalog-api__http__0"]);
        Assert.Equal(expected, vars["services__catalog-api__https__0"]);
    }

    [Fact]
    public async Task Container_with_only_an_http_endpoint_gets_only_the_http_variable()
    {
        var builder = PublishBuilder();
        var env = builder.AddPulumiAzureContainerAppEnvironment("aca-env");
        var api = Target(builder.AddContainer("orders-api", "orders").WithHttpEndpoint(targetPort: 8080), env);
        var web = Target(builder.AddContainer("web", "web").WithReference(api.GetEndpoint("http")), env);

        var vars = await EnvAsync(builder, env, web.Resource);

        Assert.Equal($"https://orders-api.internal.{Domain(env)}", vars["services__orders-api__http__0"]);
        Assert.DoesNotContain("services__orders-api__https__0", vars.Keys);
    }

    [Fact]
    public async Task Endpoint_properties_and_expressions_resolve_like_upstream()
    {
        var builder = PublishBuilder();
        var env = builder.AddPulumiAzureContainerAppEnvironment("aca-env");
        var api = Target(builder.AddContainer("orders-api", "orders").WithHttpEndpoint(targetPort: 8081).WithExternalHttpEndpoints(), env);
        var cache = Target(builder.AddContainer("cache", "redis").WithEndpoint(port: 6379, targetPort: 6379, scheme: "tcp", name: "tcp"), env);
        var http = api.GetEndpoint("http");
        var tcp = cache.GetEndpoint("tcp");
        var web = Target(builder.AddContainer("web", "web")
            .WithEnvironment("API_HOST", http.Property(EndpointProperty.Host))
            .WithEnvironment("API_PORT", http.Property(EndpointProperty.Port))
            .WithEnvironment("API_HOSTPORT", http.Property(EndpointProperty.HostAndPort))
            .WithEnvironment("API_TARGETPORT", http.Property(EndpointProperty.TargetPort))
            .WithEnvironment("API_SCHEME", http.Property(EndpointProperty.Scheme))
            .WithEnvironment("API_HEALTH", ReferenceExpression.Create($"{http}/health"))
            .WithEnvironment("CACHE_URL", tcp)
            .WithEnvironment("CACHE_HOSTPORT", tcp.Property(EndpointProperty.HostAndPort)), env);

        var vars = await EnvAsync(builder, env, web.Resource);

        var host = $"orders-api.{Domain(env)}";
        Assert.Equal(host, vars["API_HOST"]);
        Assert.Equal("443", vars["API_PORT"]);
        Assert.Equal($"{host}:443", vars["API_HOSTPORT"]);
        Assert.Equal("8081", vars["API_TARGETPORT"]);
        Assert.Equal("https", vars["API_SCHEME"]);
        Assert.Equal($"https://{host}/health", vars["API_HEALTH"]);
        // Non-HTTP endpoints are addressed by app name and port.
        Assert.Equal("tcp://cache:6379", vars["CACHE_URL"]);
        Assert.Equal("cache:6379", vars["CACHE_HOSTPORT"]);
    }

    [Fact]
    public async Task Other_environment_variables_are_unaffected()
    {
        var builder = PublishBuilder();
        var env = builder.AddPulumiAzureContainerAppEnvironment("aca-env");
        var web = Target(builder.AddContainer("web", "web")
            .WithHttpEndpoint(targetPort: 9000)
            .WithEnvironment("PLAIN", "value")
            .WithEnvironment("EXPR", ReferenceExpression.Create($"a-{"b"}")), env);

        var vars = await EnvAsync(builder, env, web.Resource);

        Assert.Equal("value", vars["PLAIN"]);
        Assert.Equal("a-b", vars["EXPR"]);
        Assert.Contains("AZURE_CLIENT_ID", vars.Keys);
    }

    [Fact]
    public async Task A_projects_own_https_endpoint_does_not_make_the_container_bind_https()
    {
        var builder = PublishBuilder();
        var env = builder.AddPulumiAzureContainerAppEnvironment("aca-env");
        var catalog = Target(builder.AddProject<CatalogProject>("catalog-api").WithHttpEndpoint().WithHttpsEndpoint(), env);
        var https = catalog.GetEndpoint("https");
        catalog.WithEnvironment("HTTP_PORTS", catalog.GetEndpoint("http").Property(EndpointProperty.TargetPort))
            .WithEnvironment("HTTPS_PORTS", https.Property(EndpointProperty.TargetPort));

        var vars = await EnvAsync(builder, env, catalog.Resource);

        Assert.Equal("8080", vars["HTTP_PORTS"]);
        Assert.DoesNotContain("HTTPS_PORTS", vars.Keys);
    }

    [Fact]
    public async Task Conditional_expressions_literal_braces_and_connection_strings_over_endpoints_resolve()
    {
        var builder = PublishBuilder();
        var env = builder.AddPulumiAzureContainerAppEnvironment("aca-env");
        var api = Target(builder.AddContainer("orders-api", "orders").WithHttpEndpoint(targetPort: 8080), env);
        var http = api.GetEndpoint("http");
        var cs = builder.AddConnectionString("orders", ReferenceExpression.Create($"Endpoint={http};Mode=x"));
        var web = Target(builder.AddContainer("web", "web")
            .WithEnvironment("BRACES", ReferenceExpression.Create($"{{literal}}-{http.Property(EndpointProperty.Scheme)}"))
            .WithEnvironment("COND", ReferenceExpression.CreateConditional(
                http.Property(EndpointProperty.Scheme), "https",
                ReferenceExpression.Create($"secure:{http.Property(EndpointProperty.Port)}"),
                ReferenceExpression.Create($"plain")))
            .WithReference(cs), env);

        var vars = await EnvAsync(builder, env, web.Resource);

        Assert.Equal("{literal}-https", vars["BRACES"]);
        Assert.Equal("secure:443", vars["COND"]);
        Assert.Equal($"Endpoint=https://orders-api.internal.{Domain(env)};Mode=x", vars["ConnectionStrings__orders"]);
    }

    [Fact]
    public async Task Existing_environments_use_the_looked_up_default_domain()
    {
        var builder = PublishBuilder();
        var env = builder.AddPulumiAzureContainerAppEnvironment("aca-env").AsExisting("shared-env", "shared-rg");
        var api = Target(builder.AddContainer("orders-api", "orders").WithHttpEndpoint(targetPort: 8080), env);
        var web = Target(builder.AddContainer("web", "web").WithReference(api.GetEndpoint("http")), env);

        var vars = await EnvAsync(builder, env, web.Resource);

        Assert.Equal($"https://orders-api.internal.{Domain(env)}", vars["services__orders-api__http__0"]);
    }

    [Fact]
    public async Task Literal_listener_settings_pass_through()
    {
        var builder = PublishBuilder();
        var env = builder.AddPulumiAzureContainerAppEnvironment("aca-env");
        var web = Target(builder.AddContainer("web", "web")
            .WithHttpEndpoint(targetPort: 8080)
            .WithEnvironment("HTTPS_PORTS", "8443")
            .WithEnvironment("Kestrel__Endpoints__grpc__Url", "http://*:9090"), env);

        var vars = await EnvAsync(builder, env, web.Resource);

        Assert.Equal("8443", vars["HTTPS_PORTS"]);
        Assert.Equal("http://*:9090", vars["Kestrel__Endpoints__grpc__Url"]);
    }

    [Fact]
    public async Task Kestrel_endpoint_overrides_from_own_endpoints_are_not_emitted_and_target_ports_stay_distinct()
    {
        var builder = PublishBuilder();
        var env = builder.AddPulumiAzureContainerAppEnvironment("aca-env");
        var api = Target(builder.AddContainer("api", "api")
            .WithHttpEndpoint(targetPort: 8080)
            .WithHttpsEndpoint(targetPort: 8443), env);
        var http = api.GetEndpoint("http");
        var https = api.GetEndpoint("https");
        // What Aspire sets for a project with Kestrel:Endpoints configuration.
        api.WithEnvironment("Kestrel__Endpoints__http__Url", ReferenceExpression.Create($"http://*:{http.Property(EndpointProperty.TargetPort)}"))
            .WithEnvironment("Kestrel__Endpoints__https__Url", ReferenceExpression.Create($"https://*:{https.Property(EndpointProperty.TargetPort)}"))
            .WithEnvironment("OWN_HTTP_TARGET", http.Property(EndpointProperty.TargetPort))
            .WithEnvironment("OWN_HTTPS_TARGET", https.Property(EndpointProperty.TargetPort));

        var vars = await EnvAsync(builder, env, api.Resource);

        // As before the fix, the app binds from its own Kestrel configuration: no two listeners forced onto one port.
        Assert.DoesNotContain("Kestrel__Endpoints__http__Url", vars.Keys);
        Assert.DoesNotContain("Kestrel__Endpoints__https__Url", vars.Keys);
        // Each endpoint reports its own target port rather than the ingress one.
        Assert.Equal("8080", vars["OWN_HTTP_TARGET"]);
        Assert.Equal("8443", vars["OWN_HTTPS_TARGET"]);
    }

    [Fact]
    public async Task Mutual_references_do_not_read_the_other_apps_pulumi_resource()
    {
        var builder = PublishBuilder();
        var env = builder.AddPulumiAzureContainerAppEnvironment("aca-env");
        var a = Target(builder.AddContainer("svc-a", "a").WithHttpEndpoint(targetPort: 8080), env);
        var b = Target(builder.AddContainer("svc-b", "b").WithHttpEndpoint(targetPort: 8080).WithExternalHttpEndpoints(), env);
        a.WithReference(b.GetEndpoint("http"));
        b.WithReference(a.GetEndpoint("http"));

        var aVars = await EnvAsync(builder, env, a.Resource);
        var bVars = await EnvAsync(builder, env, b.Resource);

        Assert.Equal($"https://svc-b.{Domain(env)}", aVars["services__svc-b__http__0"]);
        Assert.Equal($"https://svc-a.internal.{Domain(env)}", bVars["services__svc-a__http__0"]);
        // Addresses come from the app name and the environment's domain only: no dependency (or cycle) between the two apps,
        // and no nested read of an app that may not exist yet.
        var aId = AzureContainerAppEnvironmentResource.AppId("svc-a");
        var bId = AzureContainerAppEnvironmentResource.AppId("svc-b");
        Assert.DoesNotContain(aVars.Values, v => v!.Contains("${" + bId) || v.Contains("${" + aId));
        Assert.DoesNotContain(bVars.Values, v => v!.Contains("${" + aId) || v.Contains("${" + bId));
    }

    [Fact]
    public void Endpoints_outside_the_environment_are_not_mapped()
    {
        var builder = PublishBuilder();
        var env = builder.AddPulumiAzureContainerAppEnvironment("aca-env");
        var elsewhere = builder.AddContainer("elsewhere", "img").WithHttpEndpoint(targetPort: 8080);
        var job = Target(builder.AddContainer("job", "img").WithHttpEndpoint(targetPort: 8080), env);
        job.Resource.Annotations.Add(new PulumiContainerAppJobAnnotation(null));

        Assert.False(env.Resource.TryGetEndpointValue(elsewhere.GetEndpoint("http").Property(EndpointProperty.Url), out _));
        Assert.False(env.Resource.TryGetEndpointValue(job.GetEndpoint("http").Property(EndpointProperty.Url), out _));
    }
}
