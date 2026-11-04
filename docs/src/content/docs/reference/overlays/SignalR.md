---
title: "Azure.Provisioning.SignalR overlay (issue #48)"
---

Pulumi port of `Aspire.Hosting.Azure.Provisioning.SignalR`. The overlay lets you customize the SignalR resources emitted for
`AddPulumiSignalR` before the Pulumi program is written. See [provisioning.md](/Sheridan.Aspire.Pulumi/concepts/provisioning/) for the shared
`ConfigureInfrastructure` machinery.

**Packaging decision:** the overlay lives alongside the hosting project (`Sheridan.Aspire.Pulumi.Azure.SignalR`), not in a
sibling opt-in package. It was added together with the hosting port (`PulumiSignalRInfrastructure.cs`); this issue only documents it.

## Roots

| Upstream root | Pulumi property | Resource |
| --- | --- | --- |
| `SignalRService` | `infra.SignalRService` | `azure-native:signalrservice:SignalR` |

Other resources are reachable with the inherited `GetResource(id)` / `GetResources(type)`. The callback runs last in
`ContributeResourcesAsync`, after the role assignments are added.

## Example

Property paths are azure-native `signalrservice:SignalR` input names (verified with `pulumi package get-schema azure-native`;
inputs include `cors`, `features`, `networkACLs`, `tls`, `upstream`, `publicNetworkAccess`, `sku`):

```csharp
builder.AddPulumiSignalR("signalr")
    .WithPulumiStack(azureStack)
    .ConfigureInfrastructure(infra =>
    {
        infra.SignalRService.Set("cors.allowedOrigins", new[] { "https://example.com" });
        infra.SignalRService.Set("tls.clientCertEnabled", true);
        infra.SignalRService.Set("publicNetworkAccess", "Disabled");
    });
```

For example, `Set("cors.allowedOrigins", ...)`; `aspire publish` emits it under the resource's `properties` in `Pulumi.yaml`.

## Unsupported / excluded

- **Compile-time types:** the generated `Pulumi.AzureNative.SignalRService` classes cannot be used with the YAML pipeline, so
  `SignalRService` is a property bag addressed by schema names. Typos surface at `pulumi up`.
- **Polyglot (ATS) projection:** no Pulumi equivalent; the callback is C#-only.
- **Experimental opt-in gate:** `ASPIREAZUREPROVISIONING001` is not replicated.
- **`BicepValue<T>` operators:** see provisioning.md; use `PulumiExpr` and `${res.prop}` references.
