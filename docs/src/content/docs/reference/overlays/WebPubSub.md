---
title: "Azure.Provisioning.WebPubSub overlay (issue #51)"
---

Pulumi port of `Aspire.Hosting.Azure.Provisioning.WebPubSub`. The overlay lets you customize the Web PubSub resources emitted for
`AddPulumiWebPubSub` before the Pulumi program is written. See [provisioning.md](/Sheridan.Aspire.Pulumi/concepts/provisioning/) for the shared
`ConfigureInfrastructure` machinery.

**Packaging decision:** the overlay lives alongside the hosting project (`Sheridan.Aspire.Pulumi.Azure.WebPubSub`), not in a
sibling opt-in package. It was added together with the hosting port (`PulumiWebPubSubInfrastructure.cs`); this issue only documents it.

## Roots

| Upstream root | Pulumi property | Resource |
| --- | --- | --- |
| `WebPubSubService` | `infra.WebPubSubService` | `azure-native:webpubsub:WebPubSub` |

Additionally `infra.Hubs` returns the `azure-native:webpubsub:WebPubSubHub` nodes. Other resources are reachable with
`GetResource(id)` / `GetResources(type)`. The callback runs last in `ContributeResourcesAsync`, after hubs and role assignments are added.

## Example

Property paths are azure-native `webpubsub:WebPubSub` input names (verified with `pulumi package get-schema azure-native`;
inputs include `tls`, `networkACLs`, `liveTraceConfiguration`, `publicNetworkAccess`, `sku`, `socketIO`):

```csharp
builder.AddPulumiWebPubSub("pubsub")
    .WithPulumiStack(azureStack)
    .ConfigureInfrastructure(infra =>
    {
        infra.WebPubSubService.Set("tls.clientCertEnabled", false);
        infra.WebPubSubService.Set("publicNetworkAccess", "Disabled");
        foreach (var hub in infra.Hubs) hub.Set("properties.anonymousConnectPolicy", "deny");
    });
```

For example, `Set("tls.clientCertEnabled", false)`; `aspire publish` emits `tls: { clientCertEnabled: false }` under the
service's `properties` in `Pulumi.yaml`. Hub settings live under the hub's `properties` input (`WebPubSubHubProperties`).

## Unsupported / excluded

- **Compile-time types:** the generated `Pulumi.AzureNative.WebPubSub` classes cannot be used with the YAML pipeline, so
  `WebPubSubService` is a property bag addressed by schema names. Typos surface at `pulumi up`.
- **Polyglot (ATS) projection:** no Pulumi equivalent; the callback is C#-only.
- **Experimental opt-in gate:** `ASPIREAZUREPROVISIONING001` is not replicated.
- **`BicepValue<T>` operators:** see provisioning.md; use `PulumiExpr` and `${res.prop}` references.
