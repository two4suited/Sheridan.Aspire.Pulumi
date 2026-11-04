# Sheridan.Aspire.Pulumi.Azure.CosmosDB

_Cosmos DB_ — part of [Sheridan.Aspire.Pulumi](../../README.md).

`builder.AddPulumiCosmosDB("cosmos")` is the Pulumi equivalent of Aspire's `AddAzureCosmosDB`: a serverless NoSQL API account with Entra-only auth (`disableLocalAuth: true` — no connection-string keys in the cloud). Databases and containers are added the same way as the official integration:

```csharp
var cosmos = builder.AddPulumiCosmosDB("cosmos")
    .WithPulumiStack(azureStack)
    .RunAsEmulator();
cosmos.AddCosmosDatabase("appdb").AddContainer("items", "/id");

builder.AddProject<Projects.MyApi>("api")
    .WithComputeEnvironment(acaEnv)
    .WithReference(cosmos);
```

Package: `Sheridan.Aspire.Pulumi.Azure.CosmosDB` (port of `Aspire.Hosting.Azure.CosmosDB`; `AddPulumiAzureCosmosDB` remains as an alias). Also supported: `AddCosmosDatabase(name, databaseName?)`/`AddContainer(name, partitionKeyPath(s), containerName?)` with per-database/container connection strings, `WithDefaultAzureSku()` (non-serverless), `RunAsEmulator`/`RunAsPreviewEmulator`/`RunAsClassicEmulator` (+ `WithDataVolume`, `WithGatewayPort`, `WithPartitionCount`, `WithDataExplorer`), `ConfigureInfrastructure(i => i.CosmosDBAccount.Set(...))`, and `WithAccessKeyAuthentication()`, which keeps local auth on and stores the account/database/container connection strings and primary key as secrets in a Key Vault created for the account (consumers get Key Vault Secrets User; the connection string expression resolves to the secret URI, as with `AddPulumiAzureKeyVault(...).GetSecret`).

`.RunAsEmulator()` runs the Cosmos DB NoSQL API emulator container locally (no effect in publish mode) so `aspire run` works without any Azure resources. `aspire publish`/`aspire deploy` provision the real account, database(s) and container(s) instead.

`.WithReference(cosmos)` on a project is enough to get data access — no separate role-assignment call is needed. Any compute environment (e.g. `AddPulumiAzureContainerAppEnvironment`) hosting a resource that references the Cosmos DB account automatically gets a Pulumi `SqlResourceSqlRoleAssignment` granting its shared managed identity the built-in **Cosmos DB Built-in Data Contributor** role, scoped to the account — the data-plane equivalent of the ACR pull role assignment described above. `ConnectionStrings__cosmos` (the account's document endpoint, consumed by `Microsoft.Azure.Cosmos`/`Aspire.Microsoft.Azure.Cosmos` with `DefaultAzureCredential`) is read from the stack's outputs the same way `AzureAd__ClientId` is above, and resolves on the first deploy because the provision pass creates the account before the program is regenerated for the deploy pass (see "How `aspire deploy` orders things" below).
