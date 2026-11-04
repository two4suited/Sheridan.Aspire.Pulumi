# Sheridan.Aspire.Pulumi.Azure.PostgreSQL

Azure Database for PostgreSQL flexible server provisioned by Pulumi, a drop-in for `AddAzurePostgresFlexibleServer`.

`AddPulumiPostgreSQL` provisions a flexible server and the databases you add to it. Microsoft Entra authentication is used unless you call `WithPasswordAuthentication`.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.PostgreSQL
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");   // Sheridan.Aspire.Pulumi.Azure.AppContainers

var postgres = builder.AddPulumiPostgreSQL("postgres")
    .RunAsContainer();   // local Postgres for `aspire run`
var db = postgres.AddDatabase("appdb");

builder.AddProject<Projects.MyApi>("api")
    .WithComputeEnvironment(acaEnv)
    .WithReference(db);

builder.Build().Run();
```

## Features

- `AddDatabase(name, databaseName?)` adds a database to the server.
- `WithPasswordAuthentication(userName?, password?)` switches to password authentication; the connection strings are also stored in a Key Vault created for the server.
- `RunAsContainer` runs a local Postgres container in run mode.
- Alias: `AddPulumiAzurePostgresFlexibleServer`.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.PostgreSQL`](https://www.nuget.org/packages/Aspire.Hosting.Azure.PostgreSQL) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
