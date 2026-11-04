# Sheridan.Aspire.Pulumi.Azure.Redis

_Redis_ — part of [Sheridan.Aspire.Pulumi](../../README.md).

`Sheridan.Aspire.Pulumi.Azure.Redis` ports `Aspire.Hosting.Azure.Redis`. `builder.AddPulumiAzureRedis("cache")` (alias `AddPulumiRedis`) provisions Azure Cache for Redis (`azure-native:redis:Redis`, Basic C1, TLS 1.2, non-SSL port off) and `builder.AddPulumiAzureManagedRedis("cache")` (alias `AddPulumiManagedRedis`) provisions Azure Managed Redis (`redisenterprise:RedisEnterprise` Balanced_B0 plus a `default` database on port 10000), matching the Bicep the official package generates. Both default to Microsoft Entra authentication (access keys disabled): `.WithReference(cache)` yields `ConnectionStrings__cache` (`host,ssl=true` / `host:10000,ssl=true`) and the hosting compute environment's identity gets the `Data Contributor` access policy.

```csharp
var cache = builder.AddPulumiAzureRedis("cache").WithPulumiStack(azureStack).RunAsContainer();
var managed = builder.AddPulumiAzureManagedRedis("managed").WithPulumiStack(azureStack)
    .WithAccessKeyAuthentication();                 // or .WithAccessKeyAuthentication(vault) with an AddPulumiAzureKeyVault resource
```

`WithAccessKeyAuthentication()` stores `connectionstrings--<name>` (and `primaryaccesskey--<name>` for Managed Redis) in a Key Vault created next to the cache (or the vault you pass, which needs `WithDeployerRoleAssignments(AzureKeyVaultRole.KeyVaultSecretsOfficer)`), and gives referencing environments *Key Vault Secrets User*. Also: `RunAsContainer` (local Redis container), `PublishAsExisting`/`AsExisting`, `WithSku` (Managed Redis). Differences: with access keys the connection string seen by a referencing app is the Key Vault secret **URI** (keys must not appear in stack outputs; Container Apps secret references are not generated), and `AsAzureRedis`/`PublishAsAzureRedis` (converting a stock `AddRedis`) and the polyglot variant are not ported.
