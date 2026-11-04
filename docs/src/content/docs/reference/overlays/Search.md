---
title: "Search overlay (issue #46)"
---

Port of `Aspire.Hosting.Azure.Provisioning.Search`. Packaging decision: **lives alongside the hosting project**
(`Sheridan.Aspire.Pulumi.Azure.Search`); there is no sibling opt-in package. See [the provisioning guide](/Sheridan.Aspire.Pulumi/concepts/provisioning/) for the shared mechanism.

## Roots

`ConfigureInfrastructure` on `IResourceBuilder<AzureSearchResource>` hands you a `PulumiSearchInfrastructure`:

| Upstream root | Here | Pulumi type |
|---|---|---|
| `SearchService` | `infra.SearchService` | `azure-native:search:Service` |

Other resources (role assignments for referencing compute environments) are reachable via `infra.GetResource(id)` / `GetResources(type)`.
The callback runs last in `ContributeResourcesAsync`.

## Example

Paths are azure-native input properties of `search:Service` (flat, no `properties.` prefix):

```csharp
builder.AddPulumiSearch("search")
    .WithPulumiStack(stack)
    .ConfigureInfrastructure(infra =>
    {
        infra.SearchService.Set("replicaCount", 3);
        infra.SearchService.Set("semanticSearch", "free");
        infra.SearchService.Set("publicNetworkAccess", "Disabled");
        infra.SearchService.Set("networkRuleSet.bypass", "AzureServices");
    });
```


## Not supported (and why)

- Compile-time types (`SearchService`, `SearchSku`, ...): the pipeline emits Pulumi YAML, so azure-native's .NET args classes cannot be used;
  properties are a dotted-path bag checked only by `pulumi up`.
- Polyglot (ATS) projection: no Pulumi equivalent; the callback is C#-only.
- The experimental opt-in gate (`ASPIREAZUREPROVISIONING001`): not replicated.
- `BicepValue<T>` operators/ternaries: use `PulumiExpr` or `${res.prop}` strings.
