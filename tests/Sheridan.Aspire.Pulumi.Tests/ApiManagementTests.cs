using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi;
using Sheridan.Aspire.Pulumi.Azure.Base;
using Sheridan.Aspire.Pulumi.Azure.ApiManagement;
using Sheridan.Aspire.Pulumi.Azure.AppContainers;
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
    public void Interpolation_openers_in_a_document_are_escaped_for_Pulumi()
    {
        var (api, _) = NewApi();
        api.WithOpenApi("{\"d\":\"${x}\"}", AzureApiManagementOpenApiFormat.Json);
        Assert.Equal("{\"d\":\"$${x}\"}", AzureApiManagementResource.ApiProperties(api.Resource)["value"]);
    }

    [Fact]
    public void Empty_file_fails_and_unknown_extension_is_detected_by_content()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "e.txt"), "  ");
            File.WriteAllText(Path.Combine(dir, "j.txt"), "{}");
            var app = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { ProjectDirectory = dir });
            var (api, _) = NewApi(app);
            api.WithOpenApi("e.txt");
            Assert.Throws<InvalidOperationException>(() => AzureApiManagementResource.ApiProperties(api.Resource));
            api.WithOpenApi("j.txt");
            Assert.Equal("openapi+json", AzureApiManagementResource.ApiProperties(api.Resource)["format"]);
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

    private static (IResourceBuilder<AzureApiManagementResource> Apim, IDistributedApplicationBuilder App) NewApim()
    {
        var app = DistributedApplication.CreateBuilder();
        var apim = app.AddPulumiAzureApiManagement("apim", new AzureApiManagementOptions { PublisherEmail = "a@b.c", PublisherName = "p" });
        return (apim, app);
    }

    [Fact]
    public void Literal_url_backend_is_unchanged()
    {
        var (apim, _) = NewApim();
        var backend = apim.AddBackend("b", "https://example.com");
        Assert.Equal("https://example.com", backend.Resource.Url);
        Assert.Equal("https://example.com", apim.Resource.ResolveBackendUrl(backend.Resource, null));
    }

    [Fact]
    public void Compute_backend_uses_the_container_app_ingress_fqdn()
    {
        var (apim, app) = NewApim();
        var env = app.AddPulumiAzureContainerAppEnvironment("env");
        var people = app.AddContainer("people-api", "img").WithHttpEndpoint(targetPort: 8080).WithExternalHttpEndpoints().WithComputeEnvironment(env);

        var backend = apim.AddBackend("people-backend", people);

        Assert.Null(backend.Resource.Url);
        Assert.Equal("https://${peopleApiApp.configuration.ingress.fqdn}", apim.Resource.ResolveBackendUrl(backend.Resource, null));
    }

    [Fact]
    public void Compute_backend_requires_an_http_endpoint_and_a_container_apps_environment()
    {
        var (apim, app) = NewApim();
        var env = app.AddPulumiAzureContainerAppEnvironment("env");
        var noEndpoint = app.AddContainer("worker", "img").WithComputeEnvironment(env);
        var b = apim.AddBackend("worker-backend", noEndpoint);
        Assert.Throws<InvalidOperationException>(() => apim.Resource.ResolveBackendUrl(b.Resource, null));

        var noEnv = app.AddContainer("orphan", "img").WithHttpEndpoint(targetPort: 80);
        var b2 = apim.AddBackend("orphan-backend", noEnv);
        Assert.ThrowsAny<Exception>(() => apim.Resource.ResolveBackendUrl(b2.Resource, null));
    }

    [Fact]
    public void Backend_names_stay_unique_across_overloads()
    {
        var (apim, app) = NewApim();
        var env = app.AddPulumiAzureContainerAppEnvironment("env");
        var c = app.AddContainer("svc", "img").WithHttpEndpoint(targetPort: 80).WithComputeEnvironment(env);
        apim.AddBackend("b", "https://example.com");
        Assert.Throws<InvalidOperationException>(() => apim.AddBackend("b", c));
    }

    [Fact]
    public void AsExisting_records_service_and_group_without_options()
    {
        var builder = DistributedApplication.CreateBuilder();
        var apim = builder.AddPulumiAzureApiManagement("apim").AsExisting("shared-apim", "shared-rg");
        Assert.Null(apim.Resource.Options);
        Assert.Equal("shared-apim", apim.Resource.ExistingServiceName);
        Assert.Equal("shared-rg", apim.Resource.ExistingServiceResourceGroup);
        Assert.Equal("shared-rg", apim.Resource.ExistingResourceGroup);

        var other = builder.AddPulumiAzureApiManagement("apim2").PublishAsExisting("a2");
        Assert.Equal("a2", other.Resource.ExistingServiceName);
        Assert.Null(other.Resource.ExistingResourceGroup);

        Assert.Throws<ArgumentException>(() => builder.AddPulumiAzureApiManagement("apim3").AsExisting(""));
        Assert.Throws<ArgumentException>(() => builder.AddPulumiAzureApiManagement("apim4").AsExisting("a", ""));
    }

    [Fact]
    public void Existing_lookup_is_emitted_under_the_service_id_and_needs_a_group()
    {
        var builder = DistributedApplication.CreateBuilder();
        var apim = builder.AddPulumiAzureApiManagement("apim").AsExisting("shared-apim", "shared-rg");
        var program = new PulumiProgram();
        var rg = apim.Resource.AddExistingLookup(program, "${other.name}");

        Assert.Equal("shared-rg", rg);
        var invoke = (Dictionary<string, object?>)((Dictionary<string, object?>)program.Variables[apim.Resource.Id]!)["fn::invoke"]!;
        Assert.Equal("azure-native:apimanagement:getApiManagementService", invoke["function"]);
        var args = (Dictionary<string, object?>)invoke["arguments"]!;
        Assert.Equal("shared-apim", args["serviceName"]);
        Assert.Equal("shared-rg", args["resourceGroupName"]);
        Assert.DoesNotContain(apim.Resource.Id, program.Resources.Keys);

        var noGroup = builder.AddPulumiAzureApiManagement("apim2").AsExisting("a");
        Assert.Throws<InvalidOperationException>(() => noGroup.Resource.AddExistingLookup(new PulumiProgram(), "x"));
        Assert.Equal("rg-x", noGroup.WithExistingResourceGroup("rg-x").Resource.AddExistingLookup(new PulumiProgram(), "rg-x"));
    }

    [Fact]
    public void Existing_service_contributes_children_and_outputs_but_no_service()
    {
        var builder = DistributedApplication.CreateBuilder();
        var apim = builder.AddPulumiAzureApiManagement("apim").AsExisting("shared-apim", "shared-rg");
        var api = apim.AddApi("catalog", "catalog");
        apim.AddNamedValue("k", "v");
        var program = new PulumiProgram();
        apim.Resource.Contribute(program, "shared-rg", "westus");

        Assert.DoesNotContain(apim.Resource.Id, program.Resources.Keys);
        Assert.Contains(program.Resources.Values, r => r is Dictionary<string, object?> d && (string?)d["type"] == "azure-native:apimanagement:Api");
        Assert.Equal("${apim.gatewayUrl}", program.Outputs["apimGatewayUrl"]);
        Assert.Equal("${apim.name}", program.Outputs["apimName"]);
        Assert.Equal("${apim.id}", program.Outputs["apimId"]);
        Assert.DoesNotContain("apimPrincipalId", program.Outputs.Keys);
        Assert.Throws<InvalidOperationException>(() => apim.Resource.PrincipalId);
        _ = api;
    }

    [Fact]
    public void New_service_without_options_throws_at_generation()
    {
        var builder = DistributedApplication.CreateBuilder();
        var apim = builder.AddPulumiAzureApiManagement("apim");
        Assert.Throws<InvalidOperationException>(() => apim.Resource.Contribute(new PulumiProgram(), "rg", "westus"));
    }

    [Fact]
    public void ConfigureInfrastructure_service_node_throws_for_an_existing_service()
    {
        var builder = DistributedApplication.CreateBuilder();
        var apim = builder.AddPulumiAzureApiManagement("apim").AsExisting("shared-apim", "shared-rg")
            .ConfigureInfrastructure(i => _ = i.ApiManagementService);
        Assert.Throws<InvalidOperationException>(() => apim.Resource.Contribute(new PulumiProgram(), "shared-rg", "westus"));
    }

    /// <summary>Issue #204: the container app only exists after the deploy pass, so nothing that needs its FQDN may be part of the provision pass.</summary>
    [Fact]
    public void Compute_backend_and_the_policy_routed_to_it_are_left_to_the_deploy_pass()
    {
        var (apim, app) = NewApim();
        apim.WithPulumiStack(app.AddPulumiStack("s", "dev"));
        var env = app.AddPulumiAzureContainerAppEnvironment("env");
        var people = app.AddContainer("people-api", "img").WithHttpEndpoint(targetPort: 8080).WithExternalHttpEndpoints().WithComputeEnvironment(env);
        apim.AddApi("people-gateway-api", "people").WithBackend(apim.AddBackend("people-backend", people));
        apim.AddApi("catalog-api", "catalog").WithBackend(apim.AddBackend("catalog-backend", "https://catalog.example.com"));

        var program = new PulumiProgram();
        apim.Resource.Contribute(program, "rg", "westus");
        var id = apim.Resource.Id;
        var appId = AzureContainerAppEnvironmentResource.AppId("people-api");
        program.AddAppResource(appId, new() { ["type"] = "azure-native:app:ContainerApp" });

        Assert.Equal([$"{id}ApiPeopleGatewayApiPolicy", $"{id}BackendPeopleBackend"], program.GetDeferredResources().Order());

        var targets = program.GetInfrastructureTargets().Select(t => t.Name).ToList();
        Assert.DoesNotContain($"{id}BackendPeopleBackend", targets);
        Assert.DoesNotContain($"{id}ApiPeopleGatewayApiPolicy", targets);
        Assert.DoesNotContain(appId, targets);
        // The API itself, and a backend with a literal URL together with its policy, are still provisioned up front.
        Assert.Contains(id, targets);
        Assert.Contains($"{id}ApiPeopleGatewayApi", targets);
        Assert.Contains($"{id}BackendCatalogBackend", targets);
        Assert.Contains($"{id}ApiCatalogApiPolicy", targets);

        // The provision pass runs a program without them: an untargeted resource is still evaluated, and the FQDN of an
        // app that does not exist yet is nil.
        var provision = program.ToProvisionYaml("p", "d")!;
        Assert.DoesNotContain("ingress.fqdn", provision);
        Assert.DoesNotContain($"{id}BackendPeopleBackend", provision);
        Assert.DoesNotContain($"{id}ApiPeopleGatewayApiPolicy", provision);
        Assert.Contains($"  {id}BackendCatalogBackend:", provision);
        Assert.Contains($"  {id}ApiPeopleGatewayApi:", provision);
        Assert.Contains($"  {appId}:", provision);
        Assert.Contains($"https://${{{appId}.configuration.ingress.fqdn}}", program.ToYaml("p", "d"));
    }

    [Fact]
    public void Literal_backends_keep_the_provision_program_identical_to_the_full_program()
    {
        var (apim, app) = NewApim();
        apim.WithPulumiStack(app.AddPulumiStack("s", "dev"));
        apim.AddApi("catalog-api", "catalog").WithBackend(apim.AddBackend("catalog-backend", "https://catalog.example.com"));

        var program = new PulumiProgram();
        apim.Resource.Contribute(program, "rg", "westus");
        program.AddAppResource("otherApp", new() { ["type"] = "azure-native:app:ContainerApp" });

        Assert.Empty(program.GetDeferredResources());
        Assert.Null(program.ToProvisionYaml("p", "d"));
        Assert.Contains(program.GetInfrastructureTargets(), t => t.Name == $"{apim.Resource.Id}BackendCatalogBackend");
    }
}
