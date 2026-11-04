# CosmosDB overlay (issue #36)

Pulumi counterpart of `Aspire.Hosting.Azure.Provisioning.CosmosDB`. Overlays `AddPulumiCosmosDB`
(`Sheridan.Aspire.Pulumi.Azure.CosmosDB`). General mechanism: [../provisioning.md](../provisioning.md).

**Packaging decision:** the overlay lives alongside the hosting project (no sibling `...Provisioning` package, no opt-in gate).
The hook already existed (`PulumiCosmosDBInfrastructure`) and is applied last in `ContributeResourcesAsync`.

## Roots

`ConfigureInfrastructure(Action<PulumiCosmosDBInfrastructure>)` hands out:

| Upstream root | Property | azure-native resource |
| --- | --- | --- |
| `CosmosDBAccount` | `CosmosDBAccount` | `azure-native:cosmosdb:DatabaseAccount` |
| (extra) | `Databases` | `azure-native:cosmosdb:SqlResourceSqlDatabase` |
| (extra) | `Containers` | `azure-native:cosmosdb:SqlResourceSqlContainer` |

## Example

```csharp
cosmos.ConfigureInfrastructure(infra =>
{
    infra.CosmosDBAccount.Set("enableFreeTier", true);
    infra.CosmosDBAccount.Set("minimalTlsVersion", "Tls12");
    infra.CosmosDBAccount.Set("consistencyPolicy.defaultConsistencyLevel", "Session");
    foreach (var c in infra.Containers) c.Set("resource.defaultTtl", 3600);
});
```

Paths are inputs of `DatabaseAccount` (`enableFreeTier`, `consistencyPolicy`, `publicNetworkAccess`, `disableLocalAuth`, `backupPolicy`,
`capabilities`, `locations`, ...) and `SqlResourceSqlContainer` (`resource.*`, `options.*`) per `pulumi package get-schema azure-native`.
Verified with `aspire publish`: `enableFreeTier: true` appeared in the emitted `Pulumi.yaml`.

## Unsupported / excluded

- Compile-time types: upstream exposes `CosmosDBAccount` SDK models; we emit YAML, so properties are untyped dotted paths and typos
  surface at `pulumi up` (ADR 0001).
- Polyglot (ATS) projection: no Pulumi equivalent; C#-only.
- Experimental opt-in gate (`ASPIREAZUREPROVISIONING001`): not replicated.
- BicepValue operators/expressions: use `${...}` references and `PulumiExpr`.
