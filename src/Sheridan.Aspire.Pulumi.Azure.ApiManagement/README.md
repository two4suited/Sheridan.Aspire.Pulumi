# Sheridan.Aspire.Pulumi.Azure.ApiManagement

Azure API Management provisioned by Pulumi, a port of the core of `Aspire.Hosting.Azure.ApiManagement` (`AddAzureApiManagement`).

`AddPulumiAzureApiManagement` (alias `AddPulumiApiManagement`) provisions an `azure-native:apimanagement:ApiManagementService` with a system-assigned identity (outputs `gatewayUrl`, `name`, `id`, `principalId`). Set `SystemAssignedIdentity = false` on `AzureApiManagementOptions` to omit the identity and the `principalId` output (the resource's `PrincipalId` then throws): Pulumi YAML has no null-safe property access, so `${apim.identity.principalId}` fails the update on emulators that return no identity (for example floci-az):

```csharp
builder.AddPulumiAzureApiManagement("apim", new AzureApiManagementOptions
{
    PublisherEmail = "admin@example.com",
    PublisherName = "Example",
    SystemAssignedIdentity = false,
});
``` SKU and capacity are validated like the official integration (Developer by default; capacity 0 for Consumption, otherwise 1).

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.ApiManagement
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var apim = builder.AddPulumiAzureApiManagement("apim", new AzureApiManagementOptions
{
    PublisherEmail = "api-owners@example.com",
    PublisherName = "Example",
    Sku = AzureApiManagementSku.Consumption,
});

var backend = apim.AddBackend("catalog-backend", "https://catalog.example.com");
apim.AddApi("catalog-api", "catalog", subscriptionRequired: false).WithBackend(backend);

builder.Build().Run();
```

### Backend from a container app

Instead of a literal URL, point a backend at a project or container deployed to an Azure Container Apps environment (in the same Pulumi stack). The backend URL becomes `https://<container app ingress FQDN>`, a Pulumi expression on the container app (`configuration.ingress.fqdn`), so the backend is created after the app and nothing is hard-coded:

```csharp
var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("env");
var people = builder.AddDotnetProject("people-api", "../People.Api/People.Api.csproj")
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(acaEnv);

var backend = apim.AddBackend("people-backend", people);
apim.AddApi("people-api-gw", "people").WithBackend(backend);
```

Or in one call: `apim.AddApi("people-api-gw", "people", people)` adds the API, a backend named `people-api-gw-backend` for the resource, and routes the API to it (`displayName`, `subscriptionRequired` and `apiName` pass through; the backend always uses the default `http` protocol with certificate validation on and is named from `name`, so use `AddBackend` + `WithBackend` for anything else).

To point the gateway at an address other than the public FQDN (an emulator such as floci-az, an internal-only app, a custom domain), override the URL: `apim.AddBackend("people-backend", people).WithUrl("http://host.docker.internal:8080")`, or `apim.AddApi("people-api-gw", "people", people, backendUrl: "http://...")`. The override replaces the derived URL and skips all compute checks (environment, external endpoint, stack, job). A literal must be an absolute http(s) URL; it may instead contain a Pulumi expression for a resource in the same program (e.g. `http://${peopleApiApp.configuration.ingress.fqdn}`). `WithUrl` is only valid on a backend created from a compute resource.

Unless its URL is overridden, the resource needs an external http/https endpoint (`WithExternalHttpEndpoints()`) and must not be a Container Apps job. Routed APIs' policies are ordered after the backend they reference.

During `aspire deploy` (unless the URL is overridden with a literal that doesn't reference the app, in which case the backend is created in the provision step) the container app is only created in the deploy step (after its image is pushed), so a backend that points at a compute resource, and the policy of any API routed to it, is created in that step too, not in the earlier provision step. The service, the APIs and their operations, and backends with a literal URL are still created in the provision step.

## Supported

`AddApi`, `WithOpenApi` (see below), `AddOperation`, `AddBackend` (literal URL or compute resource) + `WithBackend` (a routed API with neither modeled operations nor an OpenAPI document gets wildcard proxy operations), `AddProduct` + `WithApi`, `AddNamedValue` (literal or secret), service/API/operation policies (`WithInboundPolicy`, `WithPolicy`), and `ConfigureInfrastructure` (root `ApiManagementService`).

## OpenAPI import

`WithOpenApi` imports an API's operations from an OpenAPI document (`azure-native:apimanagement:Api` `format`/`value`):

```csharp
var api = apim.AddApi("people-api", "people")
    .WithOpenApi("../People.Api/obj/People.Api.json")                 // file: JSON or YAML (by extension, else by content)
    .WithBackend(backend);

// .WithOpenApi(new Uri("https://example.com/openapi.json"))       // API Management downloads it (openapi-link)
// .WithOpenApi(jsonText, AzureApiManagementOpenApiFormat.Json)   // inline (Json -> openapi+json, Yaml -> openapi)
```

- A **file** is read when the Pulumi program is generated (publish), not when the AppHost starts, so it can be produced by a build step. Relative paths resolve against the AppHost directory; a missing file fails with an error naming the resolved path.
- With an OpenAPI document, `WithBackend` still adds the `set-backend-service` policy but **no wildcard proxy operations**: the operations come from the document. Prefer not to combine it with `AddOperation`: re-importing a changed document makes API Management replace the API's operation set, which can drop Pulumi-managed operations until the next `pulumi up`. Imported operations cannot take operation-level policies; use API-scope policies.
- The file path must point at the generated document (with `Microsoft.Extensions.ApiDescription.Server` it is written to the project's `obj/` unless `OpenApiDocumentsDirectory` is set). Interpolation openers in the document are escaped for Pulumi automatically.
- A URL import is re-run only when the URL string changes, not when the remote document does. Without `WithBackend`, API Management takes the service URL from the document's `servers`, which generated documents often point at localhost.
- **OpenAPI 3.1 caveat:** API Management supports OpenAPI 3.1 for import only and not fully. Documents emitted by `Microsoft.AspNetCore.OpenApi` (3.1) import, but 3.1-only keywords (for example `prefixItems`, `const`) and some `$ref` forms are ignored, downgraded or can fail the deployment. If an import fails, configure the generator for OpenAPI 3.0 (`OpenApiVersion = OpenApiSpecVersion.OpenApi3_0`). Swagger 2.0 documents are not supported by `WithOpenApi`. Recursive schemas are also unsupported by API Management.

## Existing service

To add APIs to a service owned elsewhere (for example another stack or resource group), call `AsExisting` (alias `PublishAsExisting`). The service is only looked up (`getApiManagementService`), never created or modified, so no publisher or SKU options are needed; the APIs, backends, products, named values and policies are created beneath it. `gatewayUrl`, `name` and `id` outputs resolve from the existing service; `principalId` is not exposed, and `ConfigureInfrastructure(...).ApiManagementService` throws (there is no resource to configure). A group passed to `AsExisting` is always the one used for the lookup and the child resources; publisher/SKU options supplied earlier are ignored. Resource group is optional if `WithExistingResourceGroup`/`WithResourceGroup` is used, and no group is created for the lookup.

```csharp
var apim = builder.AddPulumiAzureApiManagement("apim")   // no options overload
    .WithPulumiStack(stack)
    .AsExisting("shared-apim", "shared-rg");
apim.AddApi("catalog-api", "catalog");
```

Note that service-scope policies (`WithPolicy`/`WithInboundPolicy`) replace the existing service's policy; omit them to leave it untouched.

## Networking

VNet integration, private endpoints and public network access, using the `Sheridan.Aspire.Pulumi.Azure.Network` package:

```csharp
var vnet = builder.AddPulumiVirtualNetwork("vnet");

// VNet injection (Developer/Premium/PremiumV2) or outbound integration (StandardV2).
var apimSubnet = vnet.AddSubnet("apim", "10.0.2.0/24");
var apim = builder.AddPulumiAzureApiManagement("apim", new AzureApiManagementOptions
    {
        PublisherEmail = "admin@contoso.com", PublisherName = "Contoso", Sku = AzureApiManagementSku.Developer,
    })
    .WithDelegatedSubnet(apimSubnet)                                          // virtualNetworkConfiguration.subnetResourceId
    .WithVirtualNetworkType(AzureApiManagementVirtualNetworkType.Internal);   // External (default) or Internal

// Alternatively, a service outside the VNet reached through a private endpoint.
var peSubnet = vnet.AddSubnet("pe", "10.0.3.0/24");
var gateway = builder.AddPulumiAzureApiManagement("gateway", options);        // no WithDelegatedSubnet
peSubnet.AddPrivateEndpoint(gateway);                                         // group 'Gateway', zone privatelink.azure-api.net
```

- `WithDelegatedSubnet(subnet)` sets `virtualNetworkType` (External unless `WithVirtualNetworkType` says otherwise) and `virtualNetworkConfiguration.subnetResourceId`. Developer, Premium and PremiumV2 support External and Internal; StandardV2 supports External only (outbound integration). Other SKUs throw at generation time.
- `WithPublicNetworkAccess(bool)` sets `publicNetworkAccess`. Azure only accepts `Disabled` once a private endpoint exists, so create the service with public access first, then disable it. It is not switched off automatically when a private endpoint is added.
- Subnet prerequisites are not set up for you: StandardV2/PremiumV2 integration needs the subnet delegated (e.g. `WithServiceDelegation("Microsoft.Web/serverFarms")`), while classic Developer/Premium injection needs an undelegated subnet with an NSG allowing APIM management traffic. On the v2 tiers External means outbound-only integration.
- The service implements `IPulumiPrivateEndpointTarget`, so `AddPrivateEndpoint` works. Private endpoints are not supported on Consumption or VNet-injected services; generation throws for those combinations.
- Networking cannot be combined with `AsExisting`/`PublishAsExisting` (it throws at generation time).

## Not yet ported

Foundry/OpenAI/Blob backends and backend pools, policy fragments, subscriptions, Key Vault named values, Application Insights diagnostics, custom domains.

Upstream status: `Aspire.Hosting.Azure.ApiManagement` is experimental and first shipped upstream in Aspire 17.0.0 as `17.0.0-preview.1`, so it is still a preview package; track changes in [#138](https://github.com/two4suited/Sheridan.Aspire.Pulumi/issues/138).
