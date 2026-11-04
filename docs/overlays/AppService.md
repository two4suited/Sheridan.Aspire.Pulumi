# AppService overlay (issue #30)

Pulumi counterpart of upstream `Aspire.Hosting.Azure.Provisioning.AppService`, which overlays the App Service environment
integration. General mechanics are in [../provisioning.md](../provisioning.md).

**Packaging:** the overlay lives alongside the hosting project (`Sheridan.Aspire.Pulumi.Azure.AppService`,
`PulumiAppServiceInfrastructure.cs`). There is no sibling opt-in `...Provisioning` package.

## Roots

Upstream has no no-argument lookup root (plans and sites are found by identifier lookup). The same holds here:
`builder.AddPulumiAzureAppServiceEnvironment(...).ConfigureInfrastructure(infra => ...)` hands you a `PulumiAppServiceInfrastructure` with

| Lookup | Pulumi resource |
| --- | --- |
| `GetAppServicePlan()` | `azure-native:web:AppServicePlan` |
| `GetWebApp("name")` | `azure-native:web:WebApp` for the compute resource `name` |

Other resources (registry `<id>Registry`, dashboard site `<id>Dashboard`, Log Analytics, App Insights, identities) are reachable with
`infra.GetResource(id)` / `infra.GetResources(type)`. Web apps exist only for compute resources targeting the environment. The
callback runs last in `ContributeResourcesAsync`, after the sites have been added.

## Example

```csharp
builder.AddPulumiAzureAppServiceEnvironment("appsvc")
    .ConfigureInfrastructure(infra =>
    {
        infra.GetAppServicePlan().Set("zoneRedundant", true);
        infra.GetWebApp("api")
            .Set("httpsOnly", true)
            .Set("siteConfig.alwaysOn", true);
    });
```

Paths follow the azure-native `AppServicePlan` / `WebApp` input properties (verified against `pulumi package get-schema azure-native`).
Values may be literals, `PulumiExpr` results or `${res.prop}` references (the `Output<T>` equivalent).

## Unsupported / excluded

- Compile-time types: upstream exposes the `Azure.Provisioning.AppService` model classes; here properties are an untyped
  bag addressed by azure-native schema names, since the pipeline emits Pulumi YAML (a typo fails at `pulumi up`, not at compile time).
- Polyglot / ATS projection: the callback is C# only; there is no ATS equivalent for Pulumi.
- Experimental opt-in gate (`ASPIREAZUREPROVISIONING001`): not replicated; the API is always available.
- `BicepValue<T>` operators and ternaries: Pulumi YAML has none, resolve conditions in .NET.
