---
title: "Kusto overlay (issue #39)"
---

Pulumi counterpart of `Aspire.Hosting.Azure.Provisioning.Kusto`. Overlays `AddPulumiKustoCluster`
(`Sheridan.Aspire.Pulumi.Azure.Kusto`). General mechanism: [../provisioning.md](/Sheridan.Aspire.Pulumi/concepts/provisioning/).

**Packaging decision:** the overlay lives alongside the hosting project (no sibling `...Provisioning` package, no opt-in gate).
The hook already existed (`PulumiKustoInfrastructure`) and is applied last in `ContributeResourcesAsync`.

## Roots

`ConfigureInfrastructure(Action<PulumiKustoInfrastructure>)` hands out:

| Upstream root | Property | azure-native resource |
| --- | --- | --- |
| `KustoCluster` | `KustoCluster` | `azure-native:kusto:Cluster` |
| (extra) | `Databases` | `azure-native:kusto:ReadWriteDatabase` of this cluster |

## Example

```csharp
builder.AddPulumiKustoCluster("kusto")
    .WithPulumiStack(stack)
    .ConfigureInfrastructure(infra =>
    {
        infra.KustoCluster.Set("enableStreamingIngest", true);
        infra.KustoCluster.Set("sku.name", "Standard_E2ads_v5");
        infra.KustoCluster.Set("publicNetworkAccess", "Disabled");
        foreach (var db in infra.Databases) db.Set("softDeletePeriod", "P30D");
    });
```

Paths are inputs of `Cluster` (`enableStreamingIngest`, `enableAutoStop`, `enablePurge`, `sku`, `publicNetworkAccess`,
`optimizedAutoscale`, `zones`, `virtualNetworkConfiguration`, ...) per `pulumi package get-schema azure-native`;
`ReadWriteDatabase` has `softDeletePeriod` and `hotCachePeriod`.

## Unsupported / excluded

- Compile-time types: upstream exposes `KustoCluster` SDK models; we emit YAML, so properties are untyped dotted paths and typos
  surface at `pulumi up` (ADR 0001).
- Polyglot (ATS) projection: no Pulumi equivalent; C#-only.
- Experimental opt-in gate (`ASPIREAZUREPROVISIONING001`): not replicated.
- BicepValue operators/expressions: use `${...}` references and `PulumiExpr`.
