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

## Notes

- Origins must be hosted by a Container Apps or App Service environment in the **same Pulumi stack** and need an external HTTP(S) endpoint.
- Not ported: custom domains, WAF/security policies, rule sets, Premium SKU, non-Aspire origins and Bicep's `uniqueString` names.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.FrontDoor`](https://www.nuget.org/packages/Aspire.Hosting.Azure.FrontDoor) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/provisioning.md).
- Resource naming: [naming guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/naming.md).
