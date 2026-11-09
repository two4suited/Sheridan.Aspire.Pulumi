using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure.ApiManagement;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

public sealed class ApiManagementTests
{
    [Fact]
    public void Sku_defaults_to_Developer_with_one_unit()
    {
        var o = new AzureApiManagementOptions { PublisherEmail = "a@b.c", PublisherName = "p" };
        Assert.Equal(AzureApiManagementSku.Developer, o.Sku);
        Assert.Equal(1, o.Capacity);
    }

    [Fact]
    public void Consumption_defaults_to_zero_capacity()
    {
        var o = new AzureApiManagementOptions { PublisherEmail = "a@b.c", PublisherName = "p", Sku = AzureApiManagementSku.Consumption };
        Assert.Equal(0, o.Capacity);
    }

    [Fact]
    public void BuildPolicy_prefers_a_complete_document_then_wraps_inbound_statements()
    {
        Assert.Equal("<policies/>", AzureApiManagementResource.BuildPolicy("<policies/>", ["<x/>"]));
        Assert.Null(AzureApiManagementResource.BuildPolicy(null, []));
        var wrapped = AzureApiManagementResource.BuildPolicy(null, ["<rate-limit calls=\"5\" renewal-period=\"1\" />"]);
        Assert.Contains("<inbound><base /><rate-limit", wrapped);
        Assert.Contains("<on-error><base /></on-error>", wrapped);
    }

    [Fact]
    public void ValidateFragment_rejects_malformed_xml_and_blank_input()
    {
        AzureApiManagementResource.ValidateFragment("<a/><b/>", "p");
        Assert.Throws<ArgumentException>(() => AzureApiManagementResource.ValidateFragment("<a>", "p"));
        Assert.Throws<ArgumentException>(() => AzureApiManagementResource.ValidateFragment(" ", "p"));
    }

    private static (IResourceBuilder<AzureApiManagementApiResource> Api, IResourceBuilder<AzureApiManagementBackendResource> Backend) NewApi(
        IDistributedApplicationBuilder? app = null)
    {
        app ??= DistributedApplication.CreateBuilder();
        var apim = app.AddPulumiAzureApiManagement("apim", new AzureApiManagementOptions { PublisherEmail = "a@b.c", PublisherName = "p" });
        return (apim.AddApi("api", "p"), apim.AddBackend("be", "https://be.example.com"));
    }

    [Fact]
    public void Inline_json_and_yaml_map_to_openapi_formats()
    {
        var (api, _) = NewApi();
        api.WithOpenApi("{\"openapi\":\"3.1.0\"}", AzureApiManagementOpenApiFormat.Json);
        var p = AzureApiManagementResource.ApiProperties(api.Resource);
        Assert.Equal("openapi+json", p["format"]);
        Assert.Equal("{\"openapi\":\"3.1.0\"}", p["value"]);

        api.WithOpenApi("openapi: 3.1.0", AzureApiManagementOpenApiFormat.Yaml);
        Assert.Equal("openapi", AzureApiManagementResource.ApiProperties(api.Resource)["format"]);
    }

    [Fact]
    public void Url_maps_to_openapi_link()
    {
        var (api, _) = NewApi();
        api.WithOpenApi(new Uri("https://example.com/openapi.json"));
        var p = AzureApiManagementResource.ApiProperties(api.Resource);
        Assert.Equal("openapi-link", p["format"]);
        Assert.Equal("https://example.com/openapi.json", p["value"]);
        Assert.Throws<ArgumentException>(() => api.WithOpenApi(new Uri("ftp://example.com/x.json")));
    }

    [Fact]
    public void Relative_file_is_resolved_against_the_AppHost_directory_when_the_program_is_generated()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var app = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { ProjectDirectory = dir });
            var (api, _) = NewApi(app);
            api.WithOpenApi("Api.json");
            // Registered before the file exists: reading is deferred, and a missing file fails clearly.
            var ex = Assert.Throws<FileNotFoundException>(() => AzureApiManagementResource.ApiProperties(api.Resource));
            Assert.Contains(Path.Combine(dir, "Api.json"), ex.Message);

            File.WriteAllText(Path.Combine(dir, "Api.json"), "{\"openapi\":\"3.1.0\"}");
            var p = AzureApiManagementResource.ApiProperties(api.Resource);
            Assert.Equal("openapi+json", p["format"]);
            Assert.Equal("{\"openapi\":\"3.1.0\"}", p["value"]);

            File.WriteAllText(Path.Combine(dir, "Api.yaml"), "openapi: 3.1.0");
            api.WithOpenApi("Api.yaml");
            Assert.Equal("openapi", AzureApiManagementResource.ApiProperties(api.Resource)["format"]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Without_OpenApi_a_routed_API_gets_wildcard_proxy_operations()
    {
        var (api, backend) = NewApi();
        api.WithBackend(backend);
        Assert.Equal(7, AzureApiManagementResource.EffectiveOperations(api.Resource).Count);
    }

    [Fact]
    public void With_OpenApi_WithBackend_keeps_the_backend_but_adds_no_proxy_operations()
    {
        var (api, backend) = NewApi();
        api.WithOpenApi("openapi: 3.1.0", AzureApiManagementOpenApiFormat.Yaml).WithBackend(backend);
        Assert.Same(backend.Resource, api.Resource.Backend);
        Assert.Empty(AzureApiManagementResource.EffectiveOperations(api.Resource));
    }
}
