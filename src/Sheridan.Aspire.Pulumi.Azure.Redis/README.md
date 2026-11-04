# Sheridan.Aspire.Pulumi.Azure.Redis

Azure Cache for Redis and Azure Managed Redis provisioned by Pulumi, drop-ins for `AddAzureRedis` and `AddAzureManagedRedis`.

`AddPulumiAzureRedis` provisions Azure Cache for Redis (Basic C1, TLS 1.2) and `AddPulumiAzureManagedRedis` provisions Azure Managed Redis (Balanced_B0 with a `default` database on port 10000). Both default to Microsoft Entra authentication with access keys disabled.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.Redis
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var cache = builder.AddPulumiAzureRedis("cache").RunAsContainer();
var managed = builder.AddPulumiAzureManagedRedis("managed")
    .WithAccessKeyAuthentication();   // or pass an AddPulumiAzureKeyVault resource

builder.AddProject<Projects.MyApi>("api")
    .WithReference(cache);   // ConnectionStrings__cache

builder.Build().Run();
```

## Features

- The hosting compute environment's identity gets the `Data Contributor` access policy on referenced caches.
- `WithAccessKeyAuthentication()` stores the connection string (and the primary key for Managed Redis) in a Key Vault and gives referencing environments *Key Vault Secrets User*.
- `RunAsContainer` for local runs, `PublishAsExisting` / `AsExisting`, and `WithSku` (Managed Redis).
- Aliases: `AddPulumiRedis`, `AddPulumiManagedRedis`.

## Notes

- With access keys, the connection string a referencing app sees is the Key Vault secret **URI**, because keys must not appear in stack outputs.
- Not ported: `AsAzureRedis` / `PublishAsAzureRedis` (converting a stock `AddRedis`) and the polyglot variant.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.Redis`](https://www.nuget.org/packages/Aspire.Hosting.Azure.Redis) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/provisioning.md).
- Resource naming: [naming guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/naming.md).
