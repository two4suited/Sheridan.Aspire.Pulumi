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
}
