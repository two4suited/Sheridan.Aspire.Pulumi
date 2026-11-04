using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure.KeyVault;
using Sheridan.Aspire.Pulumi.Azure.Redis;

namespace Aspire.Hosting;

/// <summary>
/// Pulumi counterparts of the extension methods in the official <c>Aspire.Hosting.Azure.Redis</c> package
/// (<c>AddAzureRedis</c>, <c>AddAzureManagedRedis</c>, <c>WithAccessKeyAuthentication</c>, <c>RunAsContainer</c>).
/// </summary>
public static class PulumiAzureRedisExtensions
{
    /// <summary>Adds an Azure Cache for Redis provisioned by the Pulumi stack. Mirrors <c>AddAzureRedis</c>.</summary>
    public static IResourceBuilder<PulumiAzureRedisCacheResource> AddPulumiAzureRedis(this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new PulumiAzureRedisCacheResource(name)).ExcludeFromManifest();
    }

    /// <summary>Alias of <see cref="AddPulumiAzureRedis"/>.</summary>
    public static IResourceBuilder<PulumiAzureRedisCacheResource> AddPulumiRedis(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiAzureRedis(name);

    /// <summary>Adds an Azure Managed Redis (Redis Enterprise) cluster provisioned by the Pulumi stack. Mirrors <c>AddAzureManagedRedis</c>.</summary>
    public static IResourceBuilder<PulumiAzureManagedRedisResource> AddPulumiAzureManagedRedis(this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new PulumiAzureManagedRedisResource(name)).ExcludeFromManifest();
    }

    /// <summary>Alias of <see cref="AddPulumiAzureManagedRedis"/>.</summary>
    public static IResourceBuilder<PulumiAzureManagedRedisResource> AddPulumiManagedRedis(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiAzureManagedRedis(name);

    /// <summary>
    /// Switches from Microsoft Entra to access-key authentication. The connection string (and, for Managed Redis, the primary
    /// key) is stored in a Key Vault created next to the cache. Mirrors <c>WithAccessKeyAuthentication</c>.
    /// </summary>
    public static IResourceBuilder<T> WithAccessKeyAuthentication<T>(this IResourceBuilder<T> builder)
        where T : PulumiAzureRedisResourceBase
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Resource.UseAccessKeyAuthentication = true;
        builder.Resource.KeyVault = null;
        return builder;
    }

    /// <summary>
    /// Switches to access-key authentication and stores the secrets in <paramref name="keyVault"/> (same Pulumi stack). The
    /// deploying principal needs to write secrets there, see <c>WithDeployerRoleAssignments</c>. Mirrors <c>WithAccessKeyAuthentication(keyVault)</c>.
    /// </summary>
    public static IResourceBuilder<T> WithAccessKeyAuthentication<T>(
        this IResourceBuilder<T> builder, IResourceBuilder<PulumiAzureKeyVaultResource> keyVault)
        where T : PulumiAzureRedisResourceBase
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(keyVault);
        builder.Resource.UseAccessKeyAuthentication = true;
        builder.Resource.KeyVault = keyVault.Resource;
        return builder;
    }

    /// <summary>Overrides the Managed Redis SKU (default <c>Balanced_B0</c>).</summary>
    public static IResourceBuilder<PulumiAzureManagedRedisResource> WithSku(
        this IResourceBuilder<PulumiAzureManagedRedisResource> builder, string sku)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(sku);
        builder.Resource.Sku = sku;
        return builder;
    }

    /// <summary>
    /// Runs a local Redis container instead of connecting to the Pulumi-provisioned cache when running locally; no effect
    /// when publishing. Mirrors <c>RunAsContainer</c>.
    /// </summary>
    public static IResourceBuilder<T> RunAsContainer<T>(
        this IResourceBuilder<T> builder, Action<IResourceBuilder<RedisResource>>? configureContainer = null)
        where T : PulumiAzureRedisResourceBase
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.ApplicationBuilder.ExecutionContext.IsPublishMode) return builder;

        var container = builder.ApplicationBuilder.AddRedis($"{builder.Resource.Name}-container")
            .WithParentRelationship(builder);
        configureContainer?.Invoke(container);
        builder.Resource.Container = container.Resource;
        return builder;
    }

    /// <summary>
    /// Uses an existing cache instead of creating one (nothing is provisioned for the cache itself, but access policies and
    /// Key Vault secrets are still applied). Mirrors <c>PublishAsExisting</c>.
    /// </summary>
    public static IResourceBuilder<T> PublishAsExisting<T>(
        this IResourceBuilder<T> builder, string existingResourceName, string? resourceGroupName)
        where T : PulumiAzureRedisResourceBase
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(existingResourceName);
        ArgumentException.ThrowIfNullOrEmpty(resourceGroupName);
        builder.Resource.ExistingCacheName = existingResourceName;
        builder.Resource.ExistingResourceGroup = resourceGroupName;
        return builder;
    }

    /// <summary>Alias of <see cref="PublishAsExisting{T}"/> (the Pulumi resource has no separate local mode). Mirrors <c>AsExisting</c>.</summary>
    public static IResourceBuilder<T> AsExisting<T>(
        this IResourceBuilder<T> builder, string existingResourceName, string? resourceGroupName)
        where T : PulumiAzureRedisResourceBase =>
        builder.PublishAsExisting(existingResourceName, resourceGroupName);
}
