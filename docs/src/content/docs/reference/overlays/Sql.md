---
title: "Azure.Provisioning.Sql overlay (issue #49)"
---

Pulumi port of `Aspire.Hosting.Azure.Provisioning.Sql`. The overlay lets you customize the SQL resources emitted for
`AddPulumiSql` / `AddPulumiAzureSqlServer` before the Pulumi program is written. See [provisioning.md](/Sheridan.Aspire.Pulumi/concepts/provisioning/) for the
shared `ConfigureInfrastructure` machinery.

**Packaging decision:** the overlay lives alongside the hosting project (`Sheridan.Aspire.Pulumi.Azure.Sql`), not in a
sibling opt-in package.

## Roots

| Upstream root | Pulumi property | Resource |
| --- | --- | --- |
| `SqlServer` | `infra.SqlServer` | `azure-native:sql:Server` |
| (extra) | `infra.Databases` | `azure-native:sql:Database` nodes of this server |

`infra.SqlServer` throws for servers declared with `PublishAsExisting` (there is no Server resource, only a `getServer` lookup).
Firewall rules, elastic pools and the deployment scripts are reachable with `GetResource(id)` / `GetResources(type)`.
The callback runs last in `ContributeResourcesAsync`.

## Example

Property paths are azure-native `sql:Server` / `sql:Database` input names (verified with `pulumi package get-schema azure-native`):

```csharp
var sql = builder.AddPulumiSql("sql")
    .WithPulumiStack(azureStack)
    .ConfigureInfrastructure(infra =>
    {
        infra.SqlServer.Set("minimalTlsVersion", "1.2");
        infra.SqlServer.Set("publicNetworkAccess", "Disabled");
        infra.SqlServer.Set("administrators.azureADOnlyAuthentication", true);
        foreach (var db in infra.Databases) db.Set("minCapacity", 0.5);
    });
sql.AddDatabase("sqldb");
```

Verified with a temporary AppHost and `aspire publish`: the server's `properties` gain the changed `minimalTlsVersion` and the
database gains `minCapacity` in `Pulumi.yaml`.

## Unsupported / excluded

- **Compile-time types:** the generated `Pulumi.AzureNative.Sql` classes cannot be used with the YAML pipeline, so `SqlServer`
  is a property bag addressed by schema names. Typos or invalid enum values surface at `pulumi up`.
- **Polyglot (ATS) projection:** no Pulumi equivalent; the callback is C#-only.
- **Experimental opt-in gate:** `ASPIREAZUREPROVISIONING001` is not replicated.
- **`BicepValue<T>` operators:** see provisioning.md; use `PulumiExpr` and `${res.prop}` references.
