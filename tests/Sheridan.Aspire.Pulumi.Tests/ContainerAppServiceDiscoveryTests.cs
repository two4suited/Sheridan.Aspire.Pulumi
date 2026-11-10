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
