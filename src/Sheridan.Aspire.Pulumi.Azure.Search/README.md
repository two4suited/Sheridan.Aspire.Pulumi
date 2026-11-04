# Sheridan.Aspire.Pulumi.Azure.Search

Azure AI Search provisioned by Pulumi, a drop-in for `AddAzureSearch`.

`AddPulumiSearch` provisions a search service matching the official Bicep: `basic` SKU, 1 partition, 1 replica and local (key) auth disabled. The connection string is `Endpoint=<endpoint>`.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.Search
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");   // Sheridan.Aspire.Pulumi.Azure.AppContainers

var search = builder.AddPulumiSearch("search")
    .WithSku("standard")   // Pulumi addition
    .ConfigureInfrastructure(infra => infra.SearchService.Set("replicaCount", 3));

builder.AddProject<Projects.MyApi>("api")
    .WithComputeEnvironment(acaEnv)
    .WithReference(search);

builder.Build().Run();
```

## Features

- Outputs: `Endpoint`, `Name`, `Id`.
- `WithReference(search)` grants the compute environment's identity *Search Index Data Contributor* and *Search Service Contributor*; `WithRoleAssignments(search, AzureSearchRole....)` replaces them.
- Alias: `AddPulumiAzureSearch`.

## Notes

- Not ported: `PublishAsExisting` / `AsExisting`, and a local emulator (the official integration has none).

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.Search`](https://www.nuget.org/packages/Aspire.Hosting.Azure.Search) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
