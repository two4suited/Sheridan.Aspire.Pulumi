# Sheridan.Aspire.Pulumi.Azure.CosmosDB

Azure Cosmos DB provisioned by Pulumi, a drop-in for `AddAzureCosmosDB`.

`AddPulumiCosmosDB` provisions a serverless NoSQL account with Entra-only authentication (`disableLocalAuth: true`), so there are no connection-string keys in the cloud. Databases and containers are added as in the official integration.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.CosmosDB
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");   // Sheridan.Aspire.Pulumi.Azure.AppContainers

var cosmos = builder.AddPulumiCosmosDB("cosmos")
    .RunAsEmulator();   // local emulator for `aspire run`; real account on publish/deploy
cosmos.AddCosmosDatabase("appdb").AddContainer("items", "/id");

builder.AddProject<Projects.MyApi>("api")
    .WithComputeEnvironment(acaEnv)
    .WithReference(cosmos);

builder.Build().Run();
```

## Features

- `AddCosmosDatabase(name, databaseName?)` / `AddContainer(name, partitionKeyPath(s), containerName?)` with per-database and per-container connection strings.
- `WithReference(cosmos)` is enough for data access: the hosting compute environment's identity gets *Cosmos DB Built-in Data Contributor* scoped to the account.
- `RunAsEmulator`, `RunAsPreviewEmulator`, `RunAsClassicEmulator` plus `WithDataVolume`, `WithGatewayPort`, `WithPartitionCount`, `WithDataExplorer`.
- `WithDefaultAzureSku()` switches from serverless; `WithAccessKeyAuthentication()` keeps local auth on and stores the connection strings and key as secrets in a Key Vault created for the account.
- `ConfigureInfrastructure(i => i.CosmosDBAccount.Set(...))` edits the generated resources. Alias: `AddPulumiAzureCosmosDB`.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.CosmosDB`](https://www.nuget.org/packages/Aspire.Hosting.Azure.CosmosDB) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
