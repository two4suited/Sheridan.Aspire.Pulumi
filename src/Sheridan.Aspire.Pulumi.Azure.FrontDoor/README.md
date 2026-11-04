# Sheridan.Aspire.Pulumi.Azure.FrontDoor

_Front Door_ — part of [Sheridan.Aspire.Pulumi](../../README.md).

`Sheridan.Aspire.Pulumi.Azure.FrontDoor` ports `Aspire.Hosting.Azure.FrontDoor`. `builder.AddPulumiFrontDoor("frontdoor")` (alias `AddPulumiAzureFrontDoor`) provisions a global `azure-native:cdn:Profile` (`Standard_AzureFrontDoor`) and, for each `WithOrigin(resource)`, an `AFDEndpoint`, an `AFDOriginGroup` (health probe from the origin's `EndpointProbeAnnotation`, default `/` over HTTPS; load balancing 4/3/50), an `AFDOrigin` (host and host header = the app's FQDN) and a `/*` `Route` (HttpsOnly forwarding, HTTPS redirect, linked to the default domain), matching the generated Bicep. Each origin gets its own `*.azurefd.net` URL, output as `{frontdoor}{origin}EndpointUrl` and exposed as `GetEndpointUrl("api")`.

```csharp
var api = builder.AddProject<Projects.Api>("api").WithExternalHttpEndpoints().WithComputeEnvironment(acaEnv);
builder.AddPulumiFrontDoor("frontdoor")
    .WithPulumiStack(azureStack).WithResourceGroup(sharedRg)
    .WithOrigin(api)
    .ConfigureInfrastructure(infra => infra.GetRoute("api").Set("patternsToMatch", new[] { "/api/*" }));
```

`ConfigureInfrastructure` exposes `Profile`, `GetEndpoint/GetOriginGroup/GetOrigin/GetRoute(originName)` (see `../../docs/provisioning.md`). Origins must be hosted by an Azure Container Apps or App Service environment (the origin host is read from the emitted container app / web app) in the **same Pulumi stack**, and need an external HTTP(S) endpoint. Verified with a temporary AppHost publish. Not ported: custom domains, WAF/security policies, rule sets, Premium SKU or other origin types (e.g. non-Aspire hosts), and Bicep's deterministic `uniqueString` names (the provider auto-names the sub-resources; azure-native endpoint names are limited to 46 characters).
