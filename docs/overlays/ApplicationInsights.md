# ApplicationInsights overlay (issue #31)

Pulumi counterpart of upstream `Aspire.Hosting.Azure.Provisioning.ApplicationInsights`, which overlays the Application Insights
hosting integration. General mechanics are in [../provisioning.md](../provisioning.md).

**Packaging:** the overlay lives alongside the hosting project (`Sheridan.Aspire.Pulumi.Azure.ApplicationInsights`,
`PulumiApplicationInsightsInfrastructure.cs`). There is no sibling opt-in `...Provisioning` package.

## Roots

`builder.AddPulumiApplicationInsights(...).ConfigureInfrastructure(infra => ...)` hands you a `PulumiApplicationInsightsInfrastructure`:

| Root (upstream name) | Pulumi resource |
| --- | --- |
| `ApplicationInsightsComponent` | `azure-native:applicationinsights:Component` |
| `ImplicitLogAnalyticsWorkspace` (extra, nullable) | `azure-native:operationalinsights:Workspace` created alongside, absent when `LogAnalyticsWorkspace` was supplied |

Other resources are reachable with `infra.GetResource(id)` / `infra.GetResources(type)`. The callback runs last in
`ContributeResourcesAsync`.

## Example

```csharp
builder.AddPulumiApplicationInsights("insights")
    .ConfigureInfrastructure(infra =>
    {
        infra.ApplicationInsightsComponent
            .Set("retentionInDays", 30)
            .Set("disableIpMasking", false)
            .Set("samplingPercentage", 50);
    });
```

Paths follow the azure-native `Component` input properties (verified against `pulumi package get-schema azure-native`).
Values may be literals, `PulumiExpr` results or `${res.prop}` references (the `Output<T>` equivalent).

## Unsupported / excluded

- Compile-time types: upstream exposes the `Azure.Provisioning.ApplicationInsights` model classes; here properties are an untyped
  bag addressed by azure-native schema names, since the pipeline emits Pulumi YAML (a typo fails at `pulumi up`, not at compile time).
- Polyglot / ATS projection: the callback is C# only; there is no ATS equivalent for Pulumi.
- Experimental opt-in gate (`ASPIREAZUREPROVISIONING001`): not replicated; the API is always available.
- `BicepValue<T>` operators and ternaries: Pulumi YAML has none, resolve conditions in .NET.
