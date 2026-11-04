# Sheridan.Aspire.Pulumi.Azure.FrontDoor

Azure Front Door in front of your Container Apps or App Service apps, provisioned by Pulumi.

`AddPulumiFrontDoor` provisions a global `Standard_AzureFrontDoor` profile and, for each origin you add, an endpoint, origin group, origin and a `/*` route, matching the official generated Bicep. Each origin gets its own `*.azurefd.net` URL.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.FrontDoor
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");   // Sheridan.Aspire.Pulumi.Azure.AppContainers
var api = builder.AddProject<Projects.MyApi>("api")
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(acaEnv);

var frontDoor = builder.AddPulumiFrontDoor("frontdoor")
    .WithOrigin(api)
    .ConfigureInfrastructure(infra =>
        infra.GetRoute("api").Set("patternsToMatch", new[] { "/api/*" }));

builder.Build().Run();
```

## Features

- Health probe comes from the origin's `EndpointProbeAnnotation` (default `/` over HTTPS); routes are HTTPS-only with HTTPS redirect.
- `GetEndpointUrl("api")` returns the Front Door URL for an origin, also output as `{frontdoor}{origin}EndpointUrl`.
- `ConfigureInfrastructure` exposes `Profile` and `GetEndpoint/GetOriginGroup/GetOrigin/GetRoute(originName)`.

## Private Link to Container Apps

```csharp
acaEnv.WithAzureFrontDoorPrivateLink(frontDoor);
```

Disables public network access on the Container Apps environment, switches the profile to `Premium_AzureFrontDoor`, and gives each origin hosted in that environment a `sharedPrivateLinkResource` (group `managedEnvironments`, `enforceCertificateNameCheck`). The private endpoint uses the environment's region; if Front Door Private Link isn't available there, override `sharedPrivateLinkResource.privateLinkLocation` in `ConfigureInfrastructure`. Front Door creates the connection as **Pending**: approve it on the environment in the portal or with `az network private-endpoint-connection approve`. Unlike upstream, there is no automatic region remapping.

## Notes

- Origins must be hosted by a Container Apps or App Service environment in the **same Pulumi stack** and need an external HTTP(S) endpoint.
- Not ported: custom domains, WAF/security policies, rule sets, non-Aspire origins and Bicep's `uniqueString` names.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.FrontDoor`](https://www.nuget.org/packages/Aspire.Hosting.Azure.FrontDoor) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/provisioning.md).
- Resource naming: [naming guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/naming.md).
