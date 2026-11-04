# EventHubs overlay (issue #37)

Pulumi counterpart of `Aspire.Hosting.Azure.Provisioning.EventHubs`. Overlays `AddPulumiEventHubs`
(`Sheridan.Aspire.Pulumi.Azure.EventHubs`). General mechanism: [../provisioning.md](../provisioning.md).

**Packaging decision:** the overlay lives alongside the hosting project (no sibling `...Provisioning` package, no opt-in gate).
The hook was missing and was added (`PulumiEventHubsInfrastructure`); callbacks run last in `ContributeResourcesAsync`.

## Roots

`ConfigureInfrastructure(Action<PulumiEventHubsInfrastructure>)` hands out:

| Upstream root | Property | azure-native resource |
| --- | --- | --- |
| `EventHubsNamespace` | `EventHubsNamespace` | `azure-native:eventhub:Namespace` |
| (extra) | `EventHubs` | `azure-native:eventhub:EventHub` |
| (extra) | `ConsumerGroups` | `azure-native:eventhub:ConsumerGroup` |

## Example

```csharp
builder.AddPulumiEventHubs("eh")
    .WithPulumiStack(stack)
    .ConfigureInfrastructure(infra =>
    {
        infra.EventHubsNamespace.Set("zoneRedundant", true);
        infra.EventHubsNamespace.Set("minimumTlsVersion", "1.2");
        infra.EventHubsNamespace.Set("isAutoInflateEnabled", true);
        infra.EventHubsNamespace.Set("maximumThroughputUnits", 10);
        foreach (var hub in infra.EventHubs) hub.Set("retentionDescription.cleanupPolicy", "Delete");
    });
```

Paths are inputs of `Namespace` (`zoneRedundant`, `minimumTlsVersion`, `disableLocalAuth`, `publicNetworkAccess`, `sku`,
`isAutoInflateEnabled`, `maximumThroughputUnits`, `kafkaEnabled`, ...) and `EventHub` (`partitionCount`, `messageRetentionInDays`,
`retentionDescription`, ...) per `pulumi package get-schema azure-native`. Verified with `aspire publish`: `zoneRedundant: true`
appeared in the emitted `Pulumi.yaml`.

## Unsupported / excluded

- Compile-time types: upstream exposes `EventHubsNamespace` SDK models; we emit YAML, so properties are untyped dotted paths and typos
  surface at `pulumi up` (ADR 0001).
- Polyglot (ATS) projection: no Pulumi equivalent; C#-only.
- Experimental opt-in gate (`ASPIREAZUREPROVISIONING001`): not replicated.
- BicepValue operators/expressions: use `${...}` references and `PulumiExpr`.
