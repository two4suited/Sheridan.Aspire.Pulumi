# RedisEnterprise overlay (issue #45)

Port of `Aspire.Hosting.Azure.Provisioning.RedisEnterprise` (the Azure Managed Redis flavor). Packaging decision: **lives alongside the
hosting project** (`Sheridan.Aspire.Pulumi.Azure.Redis`); there is no sibling `...RedisEnterprise.Provisioning` package.
See `docs/provisioning.md` for the shared mechanism.

## Roots

`ConfigureInfrastructure` on `IResourceBuilder<PulumiAzureManagedRedisResource>` hands you a `PulumiRedisEnterpriseInfrastructure`:

| Upstream root | Here | Pulumi type |
|---|---|---|
| `RedisEnterpriseCluster` | `infra.RedisEnterpriseCluster` | `azure-native:redisenterprise:RedisEnterprise` |
| (extra) | `infra.Database` | `azure-native:redisenterprise:Database` (the `default` database) |

Both throw for clusters declared with `PublishAsExisting`. Access policy assignments and Key Vault secrets are reachable via
`infra.GetResource(id)` / `GetResources(type)`. The callback runs last in `ContributeResourcesAsync`.

## Example

Paths are azure-native input properties of `redisenterprise:RedisEnterprise` and `redisenterprise:Database`:

```csharp
builder.AddPulumiManagedRedis("cache")
    .WithPulumiStack(stack)
    .ConfigureInfrastructure(infra =>
    {
        infra.RedisEnterpriseCluster.Set("sku.name", "Balanced_B1");
        infra.RedisEnterpriseCluster.Set("highAvailability", "Disabled");
        infra.Database.Set("clusteringPolicy", "EnterpriseCluster");
        infra.Database.Set("persistence.aofEnabled", true);
        infra.Database.Set("persistence.aofFrequency", "1s");
    });
```

Verified with `aspire publish`: `sku.name: Balanced_B1` on the cluster and `clusteringPolicy` on the database appear in `Pulumi.yaml`.

## Not supported (and why)

- Compile-time types (`RedisEnterpriseCluster`, `RedisEnterpriseDatabase`, ...): the pipeline emits Pulumi YAML, so azure-native's .NET args
  classes cannot be used; properties are a dotted-path bag checked only by `pulumi up`.
- Polyglot (ATS) projection: no Pulumi equivalent; the callback is C#-only.
- The experimental opt-in gate (`ASPIREAZUREPROVISIONING001`): not replicated.
- `BicepValue<T>` operators/ternaries: use `PulumiExpr` or `${res.prop}` strings.
