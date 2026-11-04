---
title: "ContainerRegistry overlay (issue #34)"
---

Pulumi counterpart of `Aspire.Hosting.Azure.Provisioning.ContainerRegistry`. Overlays `AddPulumiContainerRegistry`
(`Sheridan.Aspire.Pulumi.Azure.ContainerRegistry`). General mechanism: [../provisioning.md](/Sheridan.Aspire.Pulumi/concepts/provisioning/).

**Packaging decision:** the overlay lives alongside the hosting project (no sibling `...Provisioning` package, no opt-in gate).

## Roots

`ConfigureInfrastructure(Action<PulumiContainerRegistryInfrastructure>)` hands out:

| Upstream root | Property | azure-native resource |
| --- | --- | --- |
| `ContainerRegistryService` | `ContainerRegistryService` | `azure-native:containerregistry:Registry` |

Other resources (purge tasks, role assignments) are reachable with `GetResource(id)` / `GetResources(type)`. The callback runs
last in `ContributeResourcesAsync`, after all resources and outputs have been added.

## Example

```csharp
builder.AddPulumiContainerRegistry("acr")
    .WithPulumiStack(stack)
    .ConfigureInfrastructure(infra =>
    {
        infra.ContainerRegistryService.Set("adminUserEnabled", true);
        infra.ContainerRegistryService.Set("sku.name", "Premium");
        infra.ContainerRegistryService.Set("publicNetworkAccess", "Disabled");
        infra.ContainerRegistryService.Set("zoneRedundancy", "Enabled");
    });
```

Paths are `Registry` inputs from `pulumi package get-schema azure-native` (`adminUserEnabled`, `anonymousPullEnabled`,
`dataEndpointEnabled`, `encryption`, `networkRuleSet`, `policies`, `publicNetworkAccess`, `sku`, `zoneRedundancy`, `tags`, ...).
Verified with `aspire publish`: the `Set("adminUserEnabled", true)` call changed the emitted `Pulumi.yaml`.

## Unsupported / excluded

- Compile-time types: upstream hands out `ContainerRegistryService` SDK models; we emit YAML, so azure-native .NET args classes cannot
  be used. Properties are untyped dotted paths and typos surface at `pulumi up` (see ADR 0001).
- Polyglot (ATS) projection: no Pulumi equivalent; the callback is C#-only.
- Experimental opt-in gate (`ASPIREAZUREPROVISIONING001`): not replicated.
- BicepValue operators/expressions: use Pulumi YAML `${...}` references and `PulumiExpr`.
