---
title: "AppConfiguration overlay (issue #28)"
---

Pulumi counterpart of upstream `Aspire.Hosting.Azure.Provisioning.AppConfiguration`, which overlays the App Configuration
hosting integration. General mechanics are in [../provisioning.md](/Sheridan.Aspire.Pulumi/concepts/provisioning/).

**Packaging:** the overlay lives alongside the hosting project (`Sheridan.Aspire.Pulumi.Azure.AppConfiguration`,
`PulumiAppConfigurationInfrastructure.cs`). There is no sibling opt-in `...Provisioning` package.

## Roots

`builder.AddPulumiAppConfiguration(...).ConfigureInfrastructure(infra => ...)` hands you a `PulumiAppConfigurationInfrastructure`:

| Root (upstream name) | Pulumi resource |
| --- | --- |
| `AppConfigurationStore` | `azure-native:appconfiguration:ConfigurationStore` |

Throws for stores declared with `PublishAsExisting` (no `ConfigurationStore` is emitted). Any other resource in the program is
reachable with `infra.GetResource(id)` / `infra.GetResources(type)`. The callback runs last in `ContributeResourcesAsync`.

## Example

```csharp
builder.AddPulumiAppConfiguration("appconfig")
    .ConfigureInfrastructure(infra =>
    {
        infra.AppConfigurationStore
            .Set("softDeleteRetentionInDays", 7)
            .Set("publicNetworkAccess", "Disabled")
            .Set("sku.name", "standard");
    });
```

Paths follow the azure-native `ConfigurationStore` input properties (verified against `pulumi package get-schema azure-native`).
Values may be literals, `PulumiExpr` results or `${res.prop}` references (the `Output<T>` equivalent).

## Unsupported / excluded

- Compile-time types: upstream exposes the `Azure.Provisioning.AppConfiguration` model classes; here properties are an untyped
  bag addressed by azure-native schema names, since the pipeline emits Pulumi YAML (a typo fails at `pulumi up`, not at compile time).
- Polyglot / ATS projection: the callback is C# only; there is no ATS equivalent for Pulumi.
- Experimental opt-in gate (`ASPIREAZUREPROVISIONING001`): not replicated; the API is always available.
- `BicepValue<T>` operators and ternaries: Pulumi YAML has none, resolve conditions in .NET.
