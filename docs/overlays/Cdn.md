# Cdn overlay (issue #32)

Pulumi counterpart of upstream `Aspire.Hosting.Azure.Provisioning.Cdn`, which overlays the Front Door hosting integration.
General mechanics are in [../provisioning.md](../provisioning.md).

**Packaging:** the overlay lives alongside the hosting project (`Sheridan.Aspire.Pulumi.Azure.FrontDoor`,
`PulumiFrontDoorInfrastructure.cs`). There is no sibling `...Azure.Cdn.Provisioning` package.

## Roots

`builder.AddPulumiFrontDoor(...).ConfigureInfrastructure(infra => ...)` hands you a `PulumiFrontDoorInfrastructure`:

| Root (upstream name) | Pulumi resource |
| --- | --- |
| `CdnProfile` (alias `Profile`) | `azure-native:cdn:Profile` |
| `GetEndpoint(origin)` | `azure-native:cdn:AFDEndpoint` |
| `GetOriginGroup(origin)` | `azure-native:cdn:AFDOriginGroup` |
| `GetOrigin(origin)` | `azure-native:cdn:AFDOrigin` |
| `GetRoute(origin)` | `azure-native:cdn:Route` |

`origin` is the Aspire resource name of the origin added with `WithOrigin`. The endpoint/origin group/origin/route lookups are the
identifier lookups for the per-origin resources; any other resource is reachable with `infra.GetResource(id)`. The callback runs
last in `ContributeResourcesAsync`.

## Example

```csharp
builder.AddPulumiFrontDoor("fd")
    .WithOrigin(api)
    .ConfigureInfrastructure(infra =>
    {
        infra.CdnProfile.Set("originResponseTimeoutSeconds", 120);
        infra.GetRoute("api")
            .Set("patternsToMatch", new[] { "/api/*" })
            .Set("cacheConfiguration.queryStringCachingBehavior", "IgnoreQueryString");
    });
```

Paths follow the azure-native `Profile` / `Route` input properties (verified against `pulumi package get-schema azure-native`).
Values may be literals, `PulumiExpr` results or `${res.prop}` references (the `Output<T>` equivalent).

## Unsupported / excluded

- Compile-time types: upstream exposes the `Azure.Provisioning.Cdn` model classes; here properties are an untyped
  bag addressed by azure-native schema names, since the pipeline emits Pulumi YAML (a typo fails at `pulumi up`, not at compile time).
- Polyglot / ATS projection: the callback is C# only; there is no ATS equivalent for Pulumi.
- Experimental opt-in gate (`ASPIREAZUREPROVISIONING001`): not replicated; the API is always available.
- `BicepValue<T>` operators and ternaries: Pulumi YAML has none, resolve conditions in .NET.
- Only the Front Door (Standard/Premium, `cdn:Profile` + AFD*) resources the integration creates are modelled; classic CDN
  endpoint types are not created by the hosting port, so there is nothing to overlay for them (add them with `infra.AddResource`).
