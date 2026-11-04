# Sheridan.Aspire.Pulumi.Azure.Kusto

Azure Data Explorer (Kusto) clusters provisioned by Pulumi, a drop-in for `AddAzureKustoCluster`.

`AddPulumiKustoCluster` provisions a cluster with the official defaults (`Standard_E2a_v4`, Standard tier, capacity 2) and `AddReadWriteDatabase` adds databases to it.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.Kusto
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");   // Sheridan.Aspire.Pulumi.Azure.AppContainers

var kusto = builder.AddPulumiKustoCluster("kusto");
var telemetry = kusto.AddReadWriteDatabase("telemetry")
    .WithCreationScript(".create table T (A:string)");

builder.AddProject<Projects.MyApi>("api")
    .WithComputeEnvironment(acaEnv)
    .WithReference(telemetry);   // ConnectionStrings__telemetry

builder.Build().Run();
```

## Features

- The cluster connection string is `Data Source=<clusterUri>`; a database adds `;Initial Catalog=<db>`.
- Compute environments hosting a referencing app get the database `User` principal role on every database.
- `WithSku(name, tier, capacity)` is a Pulumi addition; `WithCreationScript` provisions an `azure-native:kusto:Script`.
- Aliases: `AddPulumiAzureKustoCluster`, `AddPulumiKusto`.

## Notes

- Not ported: `RunAsEmulator` / `WithHostPort`, Kusto Explorer dashboard commands and health checks.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.Kusto`](https://www.nuget.org/packages/Aspire.Hosting.Azure.Kusto) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
