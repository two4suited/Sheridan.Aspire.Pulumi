# Sheridan.Aspire.Pulumi.Azure.KeyVault

_Key Vault_ — part of [Sheridan.Aspire.Pulumi](../../README.md).

`builder.AddPulumiAzureKeyVault("vault")` (project `Sheridan.Aspire.Pulumi.Azure.KeyVault`) is the Pulumi equivalent of `AddAzureKeyVault`: a standard-SKU, RBAC-authorized vault (same shape as the official Bicep: `tenantId`, SKU `A/standard`, `enableRbacAuthorization`, `aspire-resource-name` tag) with outputs `vaultUri`, `name`, `id`. The vault URI is the connection string, so `.WithReference(vault)` yields `ConnectionStrings__vault`.

```csharp
var vault = builder.AddPulumiAzureKeyVault("vault")
    .WithPulumiStack(azureStack)
    .AddSecret("api-key", builder.AddParameter("api-key", secret: true));

builder.AddProject<Projects.MyApi>("api")
    .WithComputeEnvironment(acaEnv)
    .WithRoleAssignments(vault, AzureKeyVaultRole.KeyVaultSecretsUser)  // optional; default is Key Vault Administrator, as in the official package
    .WithReference(vault);
```

Secret parameters become secret Pulumi config entries (`pulumi config set --secret apiKey ...`). Also supported: `PublishAsExisting`/`AsExisting`, `GetSecret(name)` (the secret URI, not a platform-resolved Key Vault reference) and the Pulumi-specific `WithDeployerRoleAssignments(...)` which grants the deploying principal roles (e.g. Secrets Officer) so Pulumi can write secrets under RBAC; secrets depend on that assignment. Not ported: `ReferenceExpression` secrets, `RunAsExisting`.
