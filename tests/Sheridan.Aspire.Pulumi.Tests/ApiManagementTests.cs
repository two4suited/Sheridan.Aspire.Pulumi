using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi;
using Sheridan.Aspire.Pulumi.Azure.Base;
using Sheridan.Aspire.Pulumi.Azure.ApiManagement;
using Sheridan.Aspire.Pulumi.Azure.AppContainers;
using Sheridan.Aspire.Pulumi.Azure.Network;
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
    public void Compute_backend_url_can_be_overridden()
    {
        var (apim, app) = NewApim();
        var env = app.AddPulumiAzureContainerAppEnvironment("env");
        var people = app.AddContainer("people-api", "img").WithHttpEndpoint(targetPort: 8080).WithComputeEnvironment(env);

        var backend = apim.AddBackend("people-backend", people).WithUrl("http://host.docker.internal:8080");
        var viaAddApi = apim.AddApi("gw", "gw", people, backendUrl: "http://internal:80");

        Assert.Throws<InvalidOperationException>(() => apim.AddBackend("lit", "https://x.example.com").WithUrl("http://y"));
        Assert.Throws<ArgumentException>(() => backend.WithUrl("foo"));
        // Overridden URLs skip the external-endpoint check (people has no external endpoint).
        Assert.Equal("http://host.docker.internal:8080", apim.Resource.ResolveBackendUrl(backend.Resource, null));
        Assert.Equal("http://internal:80", apim.Resource.ResolveBackendUrl(viaAddApi.Resource.Backend!, null));
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
    public void AddApi_with_compute_adds_the_api_a_backend_and_routes_to_it()
    {
        var (apim, app) = NewApim();
        var env = app.AddPulumiAzureContainerAppEnvironment("env");
        var people = app.AddContainer("people-api", "img").WithHttpEndpoint(targetPort: 8080).WithExternalHttpEndpoints().WithComputeEnvironment(env);

        var api = apim.AddApi("people-gw", "people", people, subscriptionRequired: false);

        var backend = Assert.Single(apim.Resource.Backends);
        Assert.Equal("people-gw-backend", backend.BackendName);
        Assert.Same(backend, api.Resource.Backend);
        Assert.Same(people.Resource, backend.Compute);
        Assert.Equal("people", api.Resource.Path);
        Assert.False(api.Resource.SubscriptionRequired);
        Assert.Equal("https://${peopleApiApp.configuration.ingress.fqdn}", apim.Resource.ResolveBackendUrl(backend, null));
    }

    [Fact]
    public void AddApi_with_compute_does_not_leave_a_half_added_api_on_name_collision()
    {
        var (apim, app) = NewApim();
        var env = app.AddPulumiAzureContainerAppEnvironment("env");
        var people = app.AddContainer("people-api", "img").WithHttpEndpoint(targetPort: 8080).WithExternalHttpEndpoints().WithComputeEnvironment(env);
        apim.AddBackend("people-gw-backend", "https://example.com");

        Assert.Throws<InvalidOperationException>(() => apim.AddApi("people-gw", "people", people));
        Assert.Empty(apim.Resource.Apis);
        Assert.Throws<ArgumentNullException>(() => apim.AddApi<ContainerResource>("x", "x", null!));
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
    public void Service_has_identity_and_principalId_output_by_default()
    {
        var (apim, app) = NewApim();
        apim.WithPulumiStack(app.AddPulumiStack("s", "dev"));
        var program = new PulumiProgram();
        apim.Resource.Contribute(program, "rg", "westus");

        var props = (Dictionary<string, object?>)((Dictionary<string, object?>)program.Resources[apim.Resource.Id]!)["properties"]!;
        Assert.Contains("identity", props.Keys);
        Assert.Equal("${apim.identity.principalId}", program.Outputs["apimPrincipalId"]);
        _ = apim.Resource.PrincipalId;
    }

    [Fact]
    public void Without_system_assigned_identity_the_identity_and_principalId_output_are_omitted()
    {
        var app = DistributedApplication.CreateBuilder();
        var apim = app.AddPulumiAzureApiManagement("apim", new AzureApiManagementOptions { PublisherEmail = "a@b.c", PublisherName = "p", SystemAssignedIdentity = false });
        apim.WithPulumiStack(app.AddPulumiStack("s", "dev"));
        var program = new PulumiProgram();
        apim.Resource.Contribute(program, "rg", "westus");

        var props = (Dictionary<string, object?>)((Dictionary<string, object?>)program.Resources[apim.Resource.Id]!)["properties"]!;
        Assert.DoesNotContain("identity", props.Keys);
        Assert.DoesNotContain("apimPrincipalId", program.Outputs.Keys);
        Assert.Equal("${apim.gatewayUrl}", program.Outputs["apimGatewayUrl"]);
        Assert.Throws<InvalidOperationException>(() => apim.Resource.PrincipalId);
    }

    [Fact]
    public void Existing_service_reports_existing_error_even_when_identity_is_disabled()
    {
        var app = DistributedApplication.CreateBuilder();
        var apim = app.AddPulumiAzureApiManagement("apim", new AzureApiManagementOptions { PublisherEmail = "a@b.c", PublisherName = "p", SystemAssignedIdentity = false }).AsExisting("a", "rg");
        var ex = Assert.Throws<InvalidOperationException>(() => apim.Resource.PrincipalId);
        Assert.Contains("existing", ex.Message);
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

    private static Dictionary<string, object?> ServiceProps(PulumiProgram program, AzureApiManagementResource r) =>
        (Dictionary<string, object?>)((Dictionary<string, object?>)program.Resources[r.Id]!)["properties"]!;

    private static (IResourceBuilder<AzureApiManagementResource> Apim, IResourceBuilder<PulumiSubnetResource> Subnet) NewNetworkedApim(AzureApiManagementSku sku)
    {
        var app = DistributedApplication.CreateBuilder();
        var subnet = app.AddPulumiVirtualNetwork("vnet").AddSubnet("apim-subnet", "10.0.2.0/24");
        var apim = app.AddPulumiAzureApiManagement("apim", new AzureApiManagementOptions { PublisherEmail = "a@b.c", PublisherName = "p", Sku = sku });
        apim.WithPulumiStack(app.AddPulumiStack("s", "dev"));
        return (apim, subnet);
    }

    [Fact]
    public void Without_networking_the_service_has_no_vnet_or_public_access_properties()
    {
        var (apim, _) = NewNetworkedApim(AzureApiManagementSku.Developer);
        var program = new PulumiProgram();
        apim.Resource.Contribute(program, "rg", "westus");
        var props = ServiceProps(program, apim.Resource);
        Assert.DoesNotContain("virtualNetworkType", props.Keys);
        Assert.DoesNotContain("virtualNetworkConfiguration", props.Keys);
        Assert.DoesNotContain("publicNetworkAccess", props.Keys);
    }

    [Fact]
    public void WithDelegatedSubnet_defaults_to_External_and_sets_the_subnet_id()
    {
        var (apim, subnet) = NewNetworkedApim(AzureApiManagementSku.Developer);
        apim.WithDelegatedSubnet(subnet);
        var program = new PulumiProgram();
        apim.Resource.Contribute(program, "rg", "westus");
        var props = ServiceProps(program, apim.Resource);
        Assert.Equal("External", props["virtualNetworkType"]);
        var cfg = (Dictionary<string, object?>)props["virtualNetworkConfiguration"]!;
        Assert.Equal(subnet.Resource.IdExpression, cfg["subnetResourceId"]);
    }

    [Fact]
    public void WithVirtualNetworkType_Internal_and_WithPublicNetworkAccess_are_emitted()
    {
        var (apim, subnet) = NewNetworkedApim(AzureApiManagementSku.Premium);
        apim.WithDelegatedSubnet(subnet).WithVirtualNetworkType(AzureApiManagementVirtualNetworkType.Internal).WithPublicNetworkAccess(false);
        var program = new PulumiProgram();
        apim.Resource.Contribute(program, "rg", "westus");
        var props = ServiceProps(program, apim.Resource);
        Assert.Equal("Internal", props["virtualNetworkType"]);
        Assert.Equal("Disabled", props["publicNetworkAccess"]);
    }

    [Theory]
    [InlineData(AzureApiManagementSku.Consumption, AzureApiManagementVirtualNetworkType.External)]
    [InlineData(AzureApiManagementSku.Basic, AzureApiManagementVirtualNetworkType.External)]
    [InlineData(AzureApiManagementSku.Standard, AzureApiManagementVirtualNetworkType.Internal)]
    [InlineData(AzureApiManagementSku.StandardV2, AzureApiManagementVirtualNetworkType.Internal)]
    public void Unsupported_sku_and_vnet_type_combinations_throw(AzureApiManagementSku sku, AzureApiManagementVirtualNetworkType type)
    {
        var (apim, subnet) = NewNetworkedApim(sku);
        apim.WithDelegatedSubnet(subnet).WithVirtualNetworkType(type);
        Assert.Throws<InvalidOperationException>(() => apim.Resource.Contribute(new PulumiProgram(), "rg", "westus"));
    }

    [Fact]
    public void StandardV2_supports_External()
    {
        var (apim, subnet) = NewNetworkedApim(AzureApiManagementSku.StandardV2);
        apim.WithDelegatedSubnet(subnet);
        apim.Resource.Contribute(new PulumiProgram(), "rg", "westus");
    }

    [Fact]
    public void Virtual_network_type_without_a_subnet_throws()
    {
        var (apim, _) = NewNetworkedApim(AzureApiManagementSku.Developer);
        apim.WithVirtualNetworkType(AzureApiManagementVirtualNetworkType.Internal);
        Assert.Throws<InvalidOperationException>(() => apim.Resource.Contribute(new PulumiProgram(), "rg", "westus"));
    }

    [Fact]
    public void Networking_on_an_existing_service_throws()
    {
        var (apim, _) = NewNetworkedApim(AzureApiManagementSku.Developer);
        apim.AsExisting("shared", "rg").WithPublicNetworkAccess(false);
        Assert.Throws<InvalidOperationException>(() => apim.Resource.Contribute(new PulumiProgram(), "rg", "westus"));
    }

    [Fact]
    public void Service_is_a_private_endpoint_target_for_the_Gateway_group()
    {
        var (apim, subnet) = NewNetworkedApim(AzureApiManagementSku.Developer);
        var target = (IPulumiPrivateEndpointTarget)apim.Resource;
        Assert.Equal("Gateway", target.PrivateLinkGroupId);
        Assert.Equal("privatelink.azure-api.net", target.PrivateDnsZoneName);
        Assert.Equal("${apim.id}", target.IdExpression);
        var pe = subnet.AddPrivateEndpoint(apim);
        Assert.Same(apim.Resource, pe.Resource.Target);
    }

    [Fact]
    public void Private_endpoint_is_rejected_for_Consumption_and_vnet_integrated_services()
    {
        var (consumption, _) = NewNetworkedApim(AzureApiManagementSku.Consumption);
        Assert.Throws<InvalidOperationException>(() => consumption.Resource.ValidatePrivateEndpointSupport());
        var (injected, subnet) = NewNetworkedApim(AzureApiManagementSku.Developer);
        injected.WithDelegatedSubnet(subnet);
        Assert.Throws<InvalidOperationException>(() => injected.Resource.ValidatePrivateEndpointSupport());
        var (plain, _) = NewNetworkedApim(AzureApiManagementSku.Developer);
        plain.Resource.ValidatePrivateEndpointSupport();
    }

    [Theory]
    [InlineData(AzureApiManagementSku.Developer, AzureApiManagementVirtualNetworkType.Internal)]
    [InlineData(AzureApiManagementSku.PremiumV2, AzureApiManagementVirtualNetworkType.External)]
    [InlineData(AzureApiManagementSku.PremiumV2, AzureApiManagementVirtualNetworkType.Internal)]
    public void Supported_sku_and_vnet_type_combinations_emit_the_type(AzureApiManagementSku sku, AzureApiManagementVirtualNetworkType type)
    {
        var (apim, subnet) = NewNetworkedApim(sku);
        apim.WithDelegatedSubnet(subnet).WithVirtualNetworkType(type);
        var program = new PulumiProgram();
        apim.Resource.Contribute(program, "rg", "westus");
        Assert.Equal(type.ToString(), ServiceProps(program, apim.Resource)["virtualNetworkType"]);
    }

    [Fact]
    public void BasicV2_does_not_support_vnet_integration_and_existing_services_reject_a_subnet()
    {
        var (basicV2, subnet) = NewNetworkedApim(AzureApiManagementSku.BasicV2);
        basicV2.WithDelegatedSubnet(subnet);
        Assert.Throws<InvalidOperationException>(() => basicV2.Resource.Contribute(new PulumiProgram(), "rg", "westus"));
        var (existing, subnet2) = NewNetworkedApim(AzureApiManagementSku.Developer);
        existing.AsExisting("shared", "rg").WithDelegatedSubnet(subnet2);
        Assert.Throws<InvalidOperationException>(() => existing.Resource.Contribute(new PulumiProgram(), "rg", "westus"));
    }
}
