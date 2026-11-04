# OperationalInsights overlay (issue #41)

Port of `Aspire.Hosting.Azure.Provisioning.OperationalInsights`. Packaging decision: **lives alongside the hosting project**
(`Sheridan.Aspire.Pulumi.Azure.OperationalInsights`); there is no sibling opt-in package. See `docs/provisioning.md` for the shared mechanism.

## Roots

`ConfigureInfrastructure` on `IResourceBuilder<LogAnalyticsWorkspaceResource>` hands you a `PulumiOperationalInsightsInfrastructure`:

| Upstream root | Here | Pulumi type |
|---|---|---|
| `OperationalInsightsWorkspace` | `infra.OperationalInsightsWorkspace` | `azure-native:operationalinsights:Workspace` |

Other resources are reachable via `infra.GetResource(id)` / `GetResources(type)` / `AddResource(id, type)`. The callback runs last in
`ContributeResourcesAsync`, after all defaults are in place.

## Example

Paths are azure-native input properties (`pulumi package get-schema azure-native`, `operationalinsights:Workspace`):

```csharp
builder.AddPulumiLogAnalyticsWorkspace("law")
    .WithPulumiStack(stack)
    .ConfigureInfrastructure(infra =>
    {
        infra.OperationalInsightsWorkspace.Set("retentionInDays", 90);
        infra.OperationalInsightsWorkspace.Set("workspaceCapping.dailyQuotaGb", 5);
        infra.OperationalInsightsWorkspace.Set("publicNetworkAccessForQuery", "Disabled");
    });
```

Verified with `aspire publish`: `retentionInDays: 90` appears on the workspace in `Pulumi.yaml`.

## Not supported (and why)

- Compile-time types (`OperationalInsightsWorkspace`, `OperationalInsightsSku`, ...): the pipeline emits Pulumi YAML, so azure-native's .NET args
  classes cannot be used; properties are a dotted-path bag checked only by `pulumi up`.
- Polyglot (ATS) projection: there is no Pulumi equivalent of the ATS type mappings; the callback is C#-only.
- The experimental opt-in gate (`ASPIREAZUREPROVISIONING001`): not replicated, the overlay is always available.
- `BicepValue<T>` operators/ternaries: use `PulumiExpr` or `${res.prop}` strings instead.
