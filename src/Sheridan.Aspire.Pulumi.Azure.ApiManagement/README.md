# Sheridan.Aspire.Pulumi.Azure.ApiManagement

Azure API Management provisioned by Pulumi, a port of the core of `Aspire.Hosting.Azure.ApiManagement` (`AddAzureApiManagement`).

`AddPulumiAzureApiManagement` (alias `AddPulumiApiManagement`) provisions an `azure-native:apimanagement:ApiManagementService` with a system-assigned identity (outputs `gatewayUrl`, `name`, `id`, `principalId`). SKU and capacity are validated like the official integration (Developer by default; capacity 0 for Consumption, otherwise 1).

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

## Supported

`AddApi`, `WithOpenApi` (see below), `AddOperation`, `AddBackend` + `WithBackend` (a routed API with neither modeled operations nor an OpenAPI document gets wildcard proxy operations), `AddProduct` + `WithApi`, `AddNamedValue` (literal or secret), service/API/operation policies (`WithInboundPolicy`, `WithPolicy`), and `ConfigureInfrastructure` (root `ApiManagementService`).

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

## Not yet ported

Compute-targeted `AddApi<T>`, Foundry/OpenAI/Blob backends and backend pools, policy fragments, subscriptions, Key Vault named values, Application Insights diagnostics, custom domains, networking.

Upstream status: `Aspire.Hosting.Azure.ApiManagement` is experimental and was merged after Aspire 13.6.4; track changes in [#138](https://github.com/two4suited/Sheridan.Aspire.Pulumi/issues/138).
