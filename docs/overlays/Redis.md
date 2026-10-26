# Redis overlay (issue #44)

Port of `Aspire.Hosting.Azure.Provisioning.Redis`. Packaging decision: **lives alongside the hosting project**
(`AspirePulumiPackages.Pulumi.Azure.Redis`); there is no sibling opt-in package. See `docs/provisioning.md` for the shared mechanism.

## Roots

`ConfigureInfrastructure` on `IResourceBuilder<PulumiAzureRedisCacheResource>` hands you a `PulumiRedisInfrastructure`:

| Upstream root | Here | Pulumi type |
|---|---|---|
| `RedisResource` | `infra.RedisResource` | `azure-native:redis:Redis` |

`RedisResource` throws for caches declared with `PublishAsExisting` (there is nothing to configure). Other resources (access policy
assignments, Key Vault and secrets for access-key auth) are reachable via `infra.GetResource(id)` / `GetResources(type)`. The callback
runs last in `ContributeResourcesAsync`. (Azure Managed Redis has its own overlay: see `RedisEnterprise.md`.)

## Example

Paths are azure-native input properties of `redis:Redis`:

```csharp
builder.AddPulumiRedis("cache")
    .WithPulumiStack(stack)
    .ConfigureInfrastructure(infra =>
    {
        infra.RedisResource.Set("sku.name", "Standard");
        infra.RedisResource.Set("sku.capacity", 2);
        infra.RedisResource.Set("redisConfiguration.maxmemoryPolicy", "allkeys-lru");
        infra.RedisResource.Set("publicNetworkAccess", "Disabled");
    });
```

Verified with `aspire publish`: `sku.capacity: 2` appears on the cache in `Pulumi.yaml`.

## Not supported (and why)

- Compile-time types (`RedisResource`, `RedisSku`, ...): the pipeline emits Pulumi YAML, so azure-native's .NET args classes cannot be used;
  properties are a dotted-path bag checked only by `pulumi up`.
- Polyglot (ATS) projection: no Pulumi equivalent; the callback is C#-only.
- The experimental opt-in gate (`ASPIREAZUREPROVISIONING001`): not replicated.
- `BicepValue<T>` operators/ternaries: use `PulumiExpr` or `${res.prop}` strings.
