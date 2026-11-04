# Sheridan.Aspire.Pulumi.Azure

Shared Azure abstractions used by the Azure integrations in the Sheridan.Aspire.Pulumi family.

This package holds the small interfaces that let integrations cooperate without referencing each other. Most apps never reference it directly; it is pulled in by compute environments such as Container Apps and App Service.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

// Compute environments (e.g. Container Apps) implement IPulumiManagedIdentityResource,
// so data integrations can grant roles to the identity of the app that references them.
var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");   // Sheridan.Aspire.Pulumi.Azure.AppContainers
var cosmos = builder.AddPulumiCosmosDB("cosmos");   // grants the environment identity data access automatically

builder.AddProject<Projects.MyApi>("api")
    .WithComputeEnvironment(acaEnv)
    .WithReference(cosmos);

builder.Build().Run();
```

## Features

- `IPulumiManagedIdentityResource`: a resource whose managed identity (principal id, name, client id) other resources in the same stack can grant data-plane roles to.
- `PulumiContainerAppCustomizationAnnotation`: a hook that lets integrations such as Azure Functions customize the container app a compute environment emits.

## Notes

- Only types shared across Azure integrations live here. Resource groups, identities and naming are in `Sheridan.Aspire.Pulumi.Azure.Base`.

## Learn more

- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/provisioning.md).
- Resource naming: [naming guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/naming.md).
