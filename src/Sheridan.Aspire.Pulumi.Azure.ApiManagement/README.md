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
    .WithOpenApi("../People.Api/People.Api.json")                 // file: JSON or YAML (by extension, else by content)
    .WithBackend(backend);

// .WithOpenApi(new Uri("https://example.com/openapi.json"))       // API Management downloads it (openapi-link)
// .WithOpenApi(jsonText, AzureApiManagementOpenApiFormat.Json)   // inline (Json -> openapi+json, Yaml -> openapi)
```

- A **file** is read when the Pulumi program is generated (publish), not when the AppHost starts, so it can be produced by a build step. Relative paths resolve against the AppHost directory; a missing file fails with an error naming the resolved path.
- With an OpenAPI document, `WithBackend` still adds the `set-backend-service` policy but **no wildcard proxy operations**: the operations come from the document. Operations added with `AddOperation` are created in addition.
- **OpenAPI 3.1 caveat:** API Management supports OpenAPI 3.1 for import only and not fully. Documents emitted by `Microsoft.AspNetCore.OpenApi` (3.1) import, but 3.1-only keywords (for example `prefixItems`, `const`) and some `$ref` forms are ignored, downgraded or can fail the deployment. If an import fails, configure the generator for OpenAPI 3.0 (`OpenApiVersion = OpenApiSpecVersion.OpenApi3_0`) or use 2.0/3.0.x. Recursive schemas are also unsupported by API Management.

## Not yet ported

Compute-targeted `AddApi<T>`, Foundry/OpenAI/Blob backends and backend pools, policy fragments, subscriptions, Key Vault named values, Application Insights diagnostics, custom domains, existing-service adoption and networking.

Upstream status: `Aspire.Hosting.Azure.ApiManagement` is experimental and was merged after Aspire 13.6.4; track changes in [#138](https://github.com/two4suited/Sheridan.Aspire.Pulumi/issues/138).
