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

builder.AddDotnetProject("api", "../MyApi/MyApi.csproj")
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(acaEnv);

builder.Build().Run();
```

## Features

- Provisions the resource group, Log Analytics workspace, container registry, an AcrPull identity and the managed environment, plus a `ContainerApp` per targeted resource.
- Customize with `WithAzureContainerRegistry`, `WithAzureLogAnalyticsWorkspace`, `WithAcrPullIdentity` and `WithDashboard`; add VNet integration with `WithDelegatedSubnet`.
- Azure emulators (e.g. floci-az) don't implement every control-plane API. `WithDashboard(false)` skips the Aspire dashboard component (`Microsoft.App/.../dotNetComponents`), and `WithoutAcrPullRoleAssignment()` omits the AcrPull role assignment (`Microsoft.Authorization/roleAssignments`) together with the apps' user-assigned identity, identity-based `registries` auth, `dependsOn` and the `AZURE_CLIENT_ID` variable, so images are pulled without registry auth. The environment's identity is still created (and referenced by outputs) but isn't attached to the apps, so data-plane role assignments made for it by other integrations have no effect on the apps (and those integrations still emit their own `roleAssignments`, which an emulator without `Microsoft.Authorization` will reject if an app references them). Don't use it against real Azure: the apps can't pull from the private registry. It also overrides `WithAcrPullIdentity` (the supplied identity isn't attached to the apps).

  ```csharp
  builder.AddPulumiAzureContainerAppEnvironment("aca-env")
         .WithDashboard(false)
         .WithoutAcrPullRoleAssignment();
  ```

- Private endpoints: the environment is an `IPulumiPrivateEndpointTarget` (group `managedEnvironments`, zone `privatelink.<location>.azurecontainerapps.io`), so `peSubnet.AddPrivateEndpoint(env)` works and sets `publicNetworkAccess: Disabled` on the environment. Azure requires a workload-profiles environment for this (the one created here is). The zone name embeds the region and is fixed when `AddPrivateEndpoint` is called, so call `WithLocation("<short region name>", e.g. westeurope)` on the environment **before** `AddPrivateEndpoint`; without it the zone is `privatelink.eastus...`, and changing `location` in stack config does not change it. For `AsExisting` environments the private endpoint is still created, but `publicNetworkAccess` is not changed and `WithLocation` must be set to the existing environment's region.
- Container Apps jobs via `PublishAsPulumiContainerAppJob` and `PublishAsScheduledPulumiContainerAppJob`.
- The environment's shared identity is granted data-plane roles by the data integrations (Cosmos DB, Storage, Service Bus, Key Vault, ...) automatically when an app references them.
- Images are tagged with a per-deploy timestamp so every deploy creates a new revision. `WithImageTag("dev")` uses a stable tag instead; note that re-deploying a rebuilt image under an unchanged tag does not create a new revision, so the new code is not rolled out until the tag changes or a restart is forced.
- `WithLocalImages()` tags the locally built image (instead of pushing it) `<loginServer>/<app>:<tag>` without pushing or running `az acr login`, for emulators (such as floci-az) that run replicas through the host Docker daemon, which uses a locally present image as-is:

```csharp
builder.AddPulumiAzureContainerAppEnvironment("aca-env")
       .WithLocalImages()
       .WithImageTag("dev");
```

## Service discovery between apps

`WithReference` to another resource deployed to the same environment sets the service-discovery variables on the caller, as the official Container Apps publisher does, so `https+http://catalog-api` resolves at runtime:

```csharp
var catalog = builder.AddDotnetProject("catalog-api", "../Catalog.Api/Catalog.Api.csproj")
    .WithComputeEnvironment(acaEnv);

builder.AddDotnetProject("web", "../Web/Web.csproj")
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
- The same mapping applies to `WithReference(target.GetEndpoint("http"))`, to endpoint values in `WithEnvironment` (`GetEndpoint(...)`, `.Property(EndpointProperty.Host | Port | HostAndPort | Scheme | TargetPort)`, and `ReferenceExpression`s built from them) and to connection strings built from such endpoints; `Port` is `443` for ingress endpoints.
- Endpoints of resources that aren't deployed to this environment, and of Container Apps jobs (which have no ingress), are not mapped. Neither are expressions that apply a format specifier to an endpoint value; those variables are left out.
- TLS ends at the ingress, which forwards plain HTTP to the port in `HTTP_PORTS`. The listener settings Aspire derives from an app's own endpoints, `HTTPS_PORTS` and `Kestrel__Endpoints__<name>__Url`, are therefore not set on the container (the app keeps its own Kestrel configuration). A literal value you set yourself, e.g. `WithEnvironment("HTTPS_PORTS", "8443")`, is passed through unchanged.
- `EndpointProperty.TargetPort` is the endpoint's own target port when it has one, otherwise the ingress target port. `EndpointProperty.TlsEnabled` is `True` for every HTTP/HTTPS endpoint, because the ingress serves it over HTTPS (upstream reports the endpoint's own setting).

Limitations compared to the official publisher:

- The app has one ingress, which forwards to the first HTTP endpoint's target port. A second HTTP endpoint on a different target port gets the same ingress URL, so it reaches the first one's port; its `TargetPort` still reports its own port, which is not exposed.
- Non-HTTP endpoints get the upstream address format, but the container app is not given additional TCP port mappings yet, so those addresses are not reachable until the ports are exposed (for example with `ConfigureInfrastructure`).

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
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://brianpsheridan.com/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://brianpsheridan.com/Sheridan.Aspire.Pulumi/concepts/naming/).
