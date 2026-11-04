---
title: "PrivateDns overlay (issue #43)"
---

Port of `Aspire.Hosting.Azure.Provisioning.PrivateDns`. Packaging decision: **lives alongside the hosting project**
(`Sheridan.Aspire.Pulumi.Azure.Network`); there is no sibling `...PrivateDns.Provisioning` package. See [the provisioning guide](/Sheridan.Aspire.Pulumi/concepts/provisioning/).

## Roots

`ConfigureInfrastructure` on `IResourceBuilder<PulumiPrivateDnsZoneResource>` hands you a `PulumiPrivateDnsInfrastructure`:

| Upstream root | Here | Pulumi type |
|---|---|---|
| `PrivateDnsZone` | `infra.PrivateDnsZone` | `azure-native:privatedns:PrivateZone` |
| (extra) | `infra.VirtualNetworkLink` | `azure-native:privatedns:VirtualNetworkLink` |

The callback runs last in the zone's `ContributeResourcesAsync`. Other network resources (subnets, NSGs, ...) belong to their own
resources and are reachable by id via `infra.GetResource(id)` only when they are in the same program.

## Example

Paths are azure-native input properties (`privatedns:PrivateZone`, `privatedns:VirtualNetworkLink`):

```csharp
builder.GetOrCreatePrivateDnsZone("privatelink.blob.core.windows.net", vnet.Resource)
    .WithPulumiStack(stack)
    .ConfigureInfrastructure(infra =>
    {
        infra.PrivateDnsZone.Set("tags.team", "platform");
        infra.VirtualNetworkLink.Set("registrationEnabled", true);
        infra.VirtualNetworkLink.Set("resolutionPolicy", "NxDomainRedirect");
    });
```

Verified with `aspire publish`: `team: "platform"` appears in the zone tags in `Pulumi.yaml`. Zones created implicitly by
`WithPrivateEndpoint` are reachable by looking them up with `GetOrCreatePrivateDnsZone` and calling `ConfigureInfrastructure` on the result.

## Not supported (and why)

- Compile-time types (`PrivateDnsZone`, `PrivateDnsARecord`, ...): the pipeline emits Pulumi YAML, so azure-native's .NET args classes cannot be
  used. Record sets are added with `infra.AddResource(id, "azure-native:privatedns:PrivateRecordSet")` and `Set(...)`.
- Polyglot (ATS) projection: no Pulumi equivalent; the callback is C#-only.
- The experimental opt-in gate (`ASPIREAZUREPROVISIONING001`): not replicated.
- `BicepValue<T>` operators/ternaries: use `PulumiExpr` or `${res.prop}` strings.
