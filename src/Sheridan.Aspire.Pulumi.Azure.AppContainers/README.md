# Sheridan.Aspire.Pulumi.Azure.AppContainers

Azure Container Apps environment provisioned by Pulumi, a drop-in for `AddAzureContainerAppEnvironment`.

`AddPulumiAzureContainerAppEnvironment` is a compute environment: resources opt in with `WithComputeEnvironment`, are built and pushed to the environment's registry, and are deployed as container apps.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.AppContainers
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");   // Sheridan.Aspire.Pulumi.Azure.AppContainers

builder.AddProject<Projects.MyApi>("api")
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(acaEnv);

builder.Build().Run();
```

## Features

- Provisions the resource group, Log Analytics workspace, container registry, an AcrPull identity and the managed environment, plus a `ContainerApp` per targeted resource.
- Customize with `WithAzureContainerRegistry`, `WithAzureLogAnalyticsWorkspace`, `WithAcrPullIdentity` and `WithDashboard`; add VNet integration with `WithDelegatedSubnet`.
- Container Apps jobs via `PublishAsPulumiContainerAppJob` and `PublishAsScheduledPulumiContainerAppJob`.
- The environment's shared identity is granted data-plane roles by the data integrations (Cosmos DB, Storage, Service Bus, Key Vault, ...) automatically when an app references them.
- Images are tagged with a per-deploy timestamp so every deploy creates a new revision.

## Service discovery between apps

`WithReference` to another resource deployed to the same environment sets the service-discovery variables on the caller, as the official Container Apps publisher does, so `https+http://catalog-api` resolves at runtime:

```csharp
var catalog = builder.AddProject<Projects.Catalog_Api>("catalog-api")
    .WithComputeEnvironment(acaEnv);

builder.AddProject<Projects.Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(catalog)              // services__catalog-api__http__0 and services__catalog-api__https__0
    .WithComputeEnvironment(acaEnv);
```

| Target endpoint | Value |
| --- | --- |
| HTTP or HTTPS, internal ingress (default) | `https://<app>.internal.<environment default domain>` |
| HTTP or HTTPS, external ingress (`WithExternalHttpEndpoints`) | `https://<app>.<environment default domain>` |
| Other schemes (e.g. `tcp`) | `<scheme>://<app>:<port>` |

- `<app>` is the target's container app name and the domain is the managed environment's `defaultDomain` (also for `AsExisting` environments).
- Both `services__<name>__http__0` and `services__<name>__https__0` carry an `https://` URL: the ingress is created with `allowInsecure: false`, so HTTP endpoints are upgraded to HTTPS, matching upstream's default.
- The same mapping applies to `WithReference(target.GetEndpoint("http"))` and to endpoint values in `WithEnvironment` (`GetEndpoint(...)`, `.Property(EndpointProperty.Host | Port | HostAndPort | Scheme | TargetPort)`, and `ReferenceExpression`s built from them); `Port` is `443` for ingress endpoints.
- Endpoints of resources that aren't deployed to this environment, and of Container Apps jobs (which have no ingress), are not mapped.

## Deploying into an existing environment

`AsExisting` (alias `PublishAsExisting`) looks up a managed environment created elsewhere with `azure-native:app:getManagedEnvironment` instead of creating it (no managed environment, Log Analytics workspace or dashboard is created). Container apps and jobs, the ACR pull identity and its AcrPull assignment are still created. The environment has no registry of its own in this mode, so pair it with `WithAzureContainerRegistry` (typically an `AsExisting` registry from the ContainerRegistry package). `WithAzureLogAnalyticsWorkspace`, `WithDelegatedSubnet`, Front Door private link and `WithDashboard(true)` aren't allowed with an existing environment (they throw at generation). Container apps and jobs are created in the existing environment's region (`location` is taken from the lookup).

```csharp
var acr = builder.AddPulumiAzureContainerRegistry("acr").AsExisting("sharedacr", "shared-rg");
var env = builder.AddPulumiAzureContainerAppEnvironment("aca-env")
    .AsExisting("shared-env", "shared-rg")   // name, resource group of the environment
    .WithAzureContainerRegistry(acr);
```

The apps and identity are created in the resource group configured for the resource (`WithResourceGroup` / `WithExistingResourceGroup`; by default a new one), which can differ from the environment's. If `resourceGroupName` is omitted, a configured group is required (otherwise generation throws), and it is also used for the lookup.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.AppContainers`](https://www.nuget.org/packages/Aspire.Hosting.Azure.AppContainers) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
