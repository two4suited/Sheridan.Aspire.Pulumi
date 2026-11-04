# Sheridan.Aspire.Pulumi.Azure.KeyVault

Azure Key Vault provisioned by Pulumi, a drop-in for `AddAzureKeyVault`.

`AddPulumiAzureKeyVault` provisions a standard-SKU, RBAC-authorized vault. The vault URI is the connection string, so `WithReference(vault)` yields `ConnectionStrings__vault`.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.KeyVault
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");   // Sheridan.Aspire.Pulumi.Azure.AppContainers

var vault = builder.AddPulumiAzureKeyVault("vault")
    .AddSecret("api-key", builder.AddParameter("api-key", secret: true));

builder.AddProject<Projects.MyApi>("api")
    .WithComputeEnvironment(acaEnv)
    .WithRoleAssignments(vault, AzureKeyVaultRole.KeyVaultSecretsUser)   // default is Key Vault Administrator, as upstream
    .WithReference(vault);

builder.Build().Run();
```

## Features

- Outputs: `vaultUri`, `name`, `id`.
- Secret parameters become secret Pulumi config entries (`pulumi config set --secret apiKey ...`).
- `GetSecret(name)` returns the secret URI. `PublishAsExisting` / `AsExisting` use an existing vault.
- `WithDeployerRoleAssignments(...)` grants the deploying principal roles (for example Secrets Officer) so Pulumi can write secrets under RBAC; secrets depend on that assignment.

## Notes

- Not ported: `ReferenceExpression` secrets and `RunAsExisting`.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.KeyVault`](https://www.nuget.org/packages/Aspire.Hosting.Azure.KeyVault) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
