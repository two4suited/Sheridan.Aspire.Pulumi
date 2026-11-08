# Sheridan.Aspire.Pulumi.Azure.ContainerRegistry

Azure Container Registry provisioned by Pulumi, a drop-in for `AddAzureContainerRegistry`.

`AddPulumiAzureContainerRegistry` provisions a registry you can share between compute environments, with an optional scheduled purge task.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.ContainerRegistry
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var registry = builder.AddPulumiAzureContainerRegistry("registry")
    .WithPurgeTask("0 1 * * *", filter: "api:.*", ago: TimeSpan.FromDays(7), keep: 5);

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env")
    .WithAzureContainerRegistry(registry);

builder.Build().Run();
```

## Features

- `WithPurgeTask(schedule, filter?, ago?, keep)` adds a scheduled `acr purge` task (five-part cron; defaults: filter `.*:.*`, ago `0d`, keep 3).
- `WithRoleAssignments(...)` grants roles on the registry using `AzureContainerRegistryRole`.
- Alias: `AddPulumiContainerRegistry`.

## Using an existing registry

`AsExisting` (alias `PublishAsExisting`) looks up a registry created elsewhere with `azure-native:containerregistry:getRegistry` instead of creating one. The name, id and login server outputs work as usual, so `WithRoleAssignments`, `WithAzureContainerRegistry` (Container Apps) and AcrPull keep working. `WithPurgeTask` isn't supported on an existing registry.

```csharp
var acr = builder.AddPulumiAzureContainerRegistry("acr")
    .AsExisting("sharedacr", "shared-rg");   // name, resource group
```

`resourceGroupName` is optional, but a group must be resolvable: when omitted, call `WithExistingResourceGroup` / `WithResourceGroup`, otherwise program generation throws. A group passed to `AsExisting` is always the one used for the lookup.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.ContainerRegistry`](https://www.nuget.org/packages/Aspire.Hosting.Azure.ContainerRegistry) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
