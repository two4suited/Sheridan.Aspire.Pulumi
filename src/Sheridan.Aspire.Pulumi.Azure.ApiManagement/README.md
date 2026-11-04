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

`AddApi`, `AddOperation`, `AddBackend` + `WithBackend` (a routed API without modeled operations gets wildcard proxy operations), `AddProduct` + `WithApi`, `AddNamedValue` (literal or secret), service/API/operation policies (`WithInboundPolicy`, `WithPolicy`), and `ConfigureInfrastructure` (root `ApiManagementService`).

## Not yet ported

Compute-targeted `AddApi<T>`, OpenAPI import, Foundry/OpenAI/Blob backends and backend pools, policy fragments, subscriptions, Key Vault named values, Application Insights diagnostics, custom domains, existing-service adoption and networking.

Upstream status: `Aspire.Hosting.Azure.ApiManagement` is experimental and was merged after Aspire 13.6.4; track changes in [#138](https://github.com/two4suited/Sheridan.Aspire.Pulumi/issues/138).
