---
title: "CognitiveServices overlay (issue #33)"
---

Pulumi counterpart of upstream `Aspire.Hosting.Azure.Provisioning.CognitiveServices`, which overlays the CognitiveServices
(Azure OpenAI) hosting integration. General mechanics are in [../provisioning.md](/Sheridan.Aspire.Pulumi/concepts/provisioning/).

**Packaging:** the overlay lives alongside the hosting project (`Sheridan.Aspire.Pulumi.Azure.CognitiveServices`,
`PulumiOpenAIInfrastructure.cs`). There is no sibling `...Azure.CognitiveServices.Provisioning` package.

## Roots

`builder.AddPulumiOpenAI(...).ConfigureInfrastructure(infra => ...)` hands you a `PulumiOpenAIInfrastructure`:

| Root (upstream name) | Pulumi resource |
| --- | --- |
| `CognitiveServicesAccount` | `azure-native:cognitiveservices:Account` (kind `OpenAI`) |
| `Deployments` (extra) | `azure-native:cognitiveservices:Deployment`, one per model deployment |

The Foundry integration (`Sheridan.Aspire.Pulumi.Azure.Foundry`) exposes the same `CognitiveServicesAccount` root on its own
`ConfigureInfrastructure`. Other resources are reachable with `infra.GetResource(id)` / `infra.GetResources(type)`. The callback
runs last in `ContributeResourcesAsync`, after role assignments have been added.

## Example

```csharp
builder.AddPulumiOpenAI("openai")
    .ConfigureInfrastructure(infra =>
    {
        infra.CognitiveServicesAccount
            .Set("properties.publicNetworkAccess", "Disabled")
            .Set("properties.networkAcls.defaultAction", "Deny");
        foreach (var d in infra.Deployments)
            d.Set("properties.versionUpgradeOption", "NoAutoUpgrade");
    });
```

Paths follow the azure-native `Account` / `Deployment` input properties (verified against `pulumi package get-schema azure-native`).
Values may be literals, `PulumiExpr` results or `${res.prop}` references (the `Output<T>` equivalent).

## Unsupported / excluded

- Compile-time types: upstream exposes the `Azure.Provisioning.CognitiveServices` model classes; here properties are an untyped
  bag addressed by azure-native schema names, since the pipeline emits Pulumi YAML (a typo fails at `pulumi up`, not at compile time).
- Polyglot / ATS projection: the callback is C# only; there is no ATS equivalent for Pulumi.
- Experimental opt-in gate (`ASPIREAZUREPROVISIONING001`): not replicated; the API is always available.
- `BicepValue<T>` operators and ternaries: Pulumi YAML has none, resolve conditions in .NET.
