# Sheridan.Aspire.Pulumi.Azure.Sql

Azure SQL Database provisioned by Pulumi, a drop-in for `AddAzureSqlServer`.

`AddPulumiSql` provisions a SQL server and the databases you add to it.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.Sql
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");   // Sheridan.Aspire.Pulumi.Azure.AppContainers

var sql = builder.AddPulumiSql("sql")
    .WithFirewallRule("office", "203.0.113.0", "203.0.113.255");
var db = sql.AddDatabase("appdb");

builder.AddProject<Projects.MyApi>("api")
    .WithComputeEnvironment(acaEnv)
    .WithReference(db);

builder.Build().Run();
```

## Features

- `AddDatabase(name, databaseName?)`, `AddElasticPool(name, poolName?, sku = "GP_Gen5_2")` and `WithElasticPool(pool)`.
- `WithFirewallRule(name, startIp, endIp)` opens the server to an address range.
- `PublishAsExisting` / `AsExisting` use an existing server.
- Aliases: `AddPulumiAzureSqlServer`.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.Sql`](https://www.nuget.org/packages/Aspire.Hosting.Azure.Sql) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/provisioning.md).
- Resource naming: [naming guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/naming.md).
