# PostgreSql overlay (issue #42)

Port of `Aspire.Hosting.Azure.Provisioning.PostgreSql`. Packaging decision: **lives alongside the hosting project**
(`AspirePulumiPackages.Pulumi.Azure.PostgreSQL`); there is no sibling opt-in package. See `docs/provisioning.md` for the shared mechanism.

## Roots

`ConfigureInfrastructure` on `IResourceBuilder<AzurePostgresFlexibleServerResource>` hands you a `PulumiPostgresInfrastructure`:

| Upstream root | Here | Pulumi type |
|---|---|---|
| `PostgreSqlFlexibleServer` | `infra.PostgreSqlFlexibleServer` | `azure-native:dbforpostgresql:Server` |
| (extra) | `infra.FirewallRules`, `infra.Databases` | `...:FirewallRule`, `...:Database` of this server |

Other resources (Key Vault, role assignments, ...) are reachable via `infra.GetResource(id)` / `GetResources(type)`. The callback runs
last in `ContributeResourcesAsync`.

## Example

Paths are azure-native input properties of `dbforpostgresql:Server`:

```csharp
builder.AddPulumiAzurePostgresFlexibleServer("pg")
    .WithPulumiStack(stack)
    .ConfigureInfrastructure(infra =>
    {
        infra.PostgreSqlFlexibleServer.Set("sku.name", "Standard_D2s_v3");
        infra.PostgreSqlFlexibleServer.Set("sku.tier", "GeneralPurpose");
        infra.PostgreSqlFlexibleServer.Set("storage.storageSizeGB", 128);
        infra.PostgreSqlFlexibleServer.Set("highAvailability.mode", "ZoneRedundant");
    });
```

## Not supported (and why)

- Compile-time types (`PostgreSqlFlexibleServer`, `PostgreSqlFlexibleServerSku`, ...): the pipeline emits Pulumi YAML, so azure-native's .NET args
  classes cannot be used; properties are a dotted-path bag checked only by `pulumi up`.
- Polyglot (ATS) projection: no Pulumi equivalent; the callback is C#-only.
- The experimental opt-in gate (`ASPIREAZUREPROVISIONING001`): not replicated.
- `BicepValue<T>` operators/ternaries: use `PulumiExpr` or `${res.prop}` strings.
