# Azure.Provisioning.ServiceBus overlay (issue #47)

Pulumi port of `Aspire.Hosting.Azure.Provisioning.ServiceBus`. The overlay lets you customize the Service Bus resources emitted
for `AddPulumiServiceBus` before the Pulumi program is written. See [provisioning.md](../provisioning.md) for the shared
`ConfigureInfrastructure` machinery.

**Packaging decision:** the overlay lives alongside the hosting project (`AspirePulumiPackages.Pulumi.Azure.ServiceBus`), not in a
sibling opt-in package.

## Roots

| Upstream root | Pulumi property | Resource |
| --- | --- | --- |
| `ServiceBusNamespace` | `infra.ServiceBusNamespace` | `azure-native:servicebus:Namespace` |

Queues, topics, subscriptions and rules are reachable through the inherited lookups (`GetResource(id)`, `GetResources(type)`),
for example `infra.GetResources("azure-native:servicebus:Queue")`.

## Example

Property paths are azure-native `servicebus:Namespace` input names (verified with `pulumi package get-schema azure-native`):

```csharp
builder.AddPulumiServiceBus("sb")
    .WithPulumiStack(azureStack)
    .ConfigureInfrastructure(infra =>
    {
        infra.ServiceBusNamespace.Set("minimumTlsVersion", "1.2");
        infra.ServiceBusNamespace.Set("publicNetworkAccess", "Disabled");
        infra.ServiceBusNamespace.Set("sku.name", "Premium");
        infra.ServiceBusNamespace.Set("sku.capacity", 1);
        infra.ServiceBusNamespace.Set("zoneRedundant", true);
    });
```

Verified with `aspire publish`: the namespace's `properties` gain `minimumTlsVersion: "1.2"` in the generated `Pulumi.yaml`.
The callback runs last in `ContributeResourcesAsync`, so it sees (and can override) everything the port emitted, including role assignments.

## Unsupported / excluded

- **Compile-time types:** the YAML pipeline cannot use the generated `Pulumi.AzureNative.ServiceBus` classes, so `ServiceBusNamespace` is a
  property bag addressed by schema names. Typos surface at `pulumi up`, not at build time.
- **Polyglot (ATS) projection:** upstream projects the SDK models into non-C# AppHosts; there is no Pulumi equivalent, the callback is C#-only.
- **Experimental opt-in gate:** `ASPIREAZUREPROVISIONING001` is not replicated; the API is always available.
- **`BicepValue<T>` operators:** see provisioning.md; use `PulumiExpr` and `${res.prop}` references instead.
