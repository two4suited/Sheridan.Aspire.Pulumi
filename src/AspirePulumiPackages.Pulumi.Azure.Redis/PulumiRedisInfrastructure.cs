using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Provisioning;

namespace AspirePulumiPackages.Pulumi.Azure.Redis;

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi Azure Cache for Redis. Port of the Azure.Provisioning
/// Redis overlay (root: <c>RedisResource</c>). Properties use azure-native names, e.g.
/// <c>RedisResource.Set("sku.capacity", 2)</c>.
/// </summary>
public sealed class PulumiRedisInfrastructure(PulumiInfrastructure inner, PulumiAzureRedisCacheResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:redis:Redis</c>. Throws for caches declared with PublishAsExisting.</summary>
    public PulumiResourceNode RedisResource => owner.ExistingCacheName is null
        ? GetResource(owner.Id)
        : throw new InvalidOperationException($"'{owner.Name}' is an existing cache; there is no Redis resource to configure.");
}

/// <summary>
/// Infrastructure handed to <c>ConfigureInfrastructure</c> on a Pulumi Azure Managed Redis. Port of the Azure.Provisioning
/// RedisEnterprise overlay (root: <c>RedisEnterpriseCluster</c>). Properties use azure-native names, e.g.
/// <c>RedisEnterpriseCluster.Set("sku.name", "Balanced_B1")</c>.
/// </summary>
public sealed class PulumiRedisEnterpriseInfrastructure(PulumiInfrastructure inner, PulumiAzureManagedRedisResource owner) : PulumiInfrastructure(inner.Program)
{
    /// <summary>The <c>azure-native:redisenterprise:RedisEnterprise</c> cluster. Throws for caches declared with PublishAsExisting.</summary>
    public PulumiResourceNode RedisEnterpriseCluster => owner.ExistingCacheName is null
        ? GetResource(owner.Id)
        : throw new InvalidOperationException($"'{owner.Name}' is an existing cluster; there is no RedisEnterprise resource to configure.");

    /// <summary>The <c>default</c> database (<c>azure-native:redisenterprise:Database</c>). Throws for existing clusters.</summary>
    public PulumiResourceNode Database => owner.ExistingCacheName is null
        ? GetResource($"{owner.Id}Default")
        : throw new InvalidOperationException($"'{owner.Name}' is an existing cluster; there is no Database resource to configure.");
}

public static class PulumiRedisInfrastructureExtensions
{
    /// <summary>Pulumi equivalent of the overlay's <c>ConfigureInfrastructure</c>: customize the Redis cache before the program is emitted.</summary>
    public static IResourceBuilder<PulumiAzureRedisCacheResource> ConfigureInfrastructure(
        this IResourceBuilder<PulumiAzureRedisCacheResource> builder, Action<PulumiRedisInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiRedisInfrastructure(infra, owner)));
    }

    /// <summary>Pulumi equivalent of the RedisEnterprise overlay's <c>ConfigureInfrastructure</c>: customize the Managed Redis cluster before the program is emitted.</summary>
    public static IResourceBuilder<PulumiAzureManagedRedisResource> ConfigureInfrastructure(
        this IResourceBuilder<PulumiAzureManagedRedisResource> builder, Action<PulumiRedisEnterpriseInfrastructure> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var owner = builder.Resource;
        return PulumiInfrastructureExtensions.ConfigureInfrastructure(builder, infra => configure(new PulumiRedisEnterpriseInfrastructure(infra, owner)));
    }
}
