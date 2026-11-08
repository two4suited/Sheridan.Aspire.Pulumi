using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Utils;

namespace Sheridan.Aspire.Pulumi.Azure.Storage;

/// <summary>Pulumi equivalents of the Aspire.Hosting.Azure.Storage extension methods.</summary>
public static class AzureStorageExtensions
{
    /// <summary>Adds an Azure Storage account provisioned by the Pulumi stack (the equivalent of AddAzureStorage).</summary>
    public static IResourceBuilder<AzureStorageResource> AddPulumiStorage(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddResource(new AzureStorageResource(name)).ExcludeFromManifest();

    /// <summary>Alias of <see cref="AddPulumiStorage"/> following the <c>AddPulumiAzureX</c> naming used by the other Azure integrations.</summary>
    public static IResourceBuilder<AzureStorageResource> AddPulumiAzureStorage(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiStorage(name);

    /// <summary>
    /// Uses an existing storage account instead of creating one: it is looked up by name and resource group, and the containers,
    /// queues and role assignments are still applied to it. Mirrors <c>PublishAsExisting</c>.
    /// </summary>
    /// <param name="existingResourceName">Name of the existing storage account.</param>
    /// <param name="resourceGroupName">Resource group of the account; when omitted, the resource group configured for this resource (<c>WithResourceGroup</c>/<c>WithExistingResourceGroup</c>) is used.</param>
    public static IResourceBuilder<AzureStorageResource> PublishAsExisting(
        this IResourceBuilder<AzureStorageResource> builder, string existingResourceName, string? resourceGroupName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(existingResourceName);
        if (resourceGroupName is not null) ArgumentException.ThrowIfNullOrEmpty(resourceGroupName);
        builder.Resource.ExistingAccountName = existingResourceName;
        builder.Resource.ExistingAccountResourceGroup = resourceGroupName;
        // Also marks a resource group as configured so none is created just for the lookup.
        if (resourceGroupName is not null) builder.Resource.ExistingResourceGroup = resourceGroupName;
        return builder;
    }

    /// <summary>Alias of <see cref="PublishAsExisting"/>. For local runs, add <c>RunAsExisting</c> or <c>RunAsEmulator</c>. Mirrors <c>AsExisting</c>.</summary>
    public static IResourceBuilder<AzureStorageResource> AsExisting(
        this IResourceBuilder<AzureStorageResource> builder, string existingResourceName, string? resourceGroupName = null) =>
        builder.PublishAsExisting(existingResourceName, resourceGroupName);

    /// <summary>
    /// For an <c>AsExisting</c>/<c>PublishAsExisting</c> account: uses the blob, queue and table endpoints of <paramref name="accountNameOrEndpoint"/>
    /// (a bare account name, a host such as <c>myaccount.blob.core.windows.net</c>, or a service URL) when running locally,
    /// so no generated Pulumi project or deployed stack output is needed. Clients authenticate with the developer's Azure credentials.
    /// No effect in publish mode.
    /// </summary>
    public static IResourceBuilder<AzureStorageResource> RunAsExisting(
        this IResourceBuilder<AzureStorageResource> builder, string accountNameOrEndpoint)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(accountNameOrEndpoint);
        var (account, suffix) = ParseAccount(accountNameOrEndpoint);
        return builder.RunAsExistingCore(Endpoints(account, suffix));
    }

    /// <summary>Like <see cref="RunAsExisting(IResourceBuilder{AzureStorageResource}, string)"/> with the storage account name supplied by a parameter.</summary>
    public static IResourceBuilder<AzureStorageResource> RunAsExisting(
        this IResourceBuilder<AzureStorageResource> builder, IResourceBuilder<ParameterResource> accountName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(accountName);
        var name = accountName.Resource;
        return builder.RunAsExistingCore(new StorageRunEndpoints(
            ReferenceExpression.Create($"https://{name}.blob.core.windows.net/"),
            ReferenceExpression.Create($"https://{name}.queue.core.windows.net/"),
            ReferenceExpression.Create($"https://{name}.table.core.windows.net/"),
            ReferenceExpression.Create($"https://{name}.dfs.core.windows.net/")));
    }

    private static IResourceBuilder<AzureStorageResource> RunAsExistingCore(
        this IResourceBuilder<AzureStorageResource> builder, StorageRunEndpoints endpoints)
    {
        if (builder.Resource.Emulator is not null)
            throw new InvalidOperationException("The Azure Storage resource is already configured to run as an emulator.");
        if (builder.Resource.RunEndpoints is not null)
            throw new InvalidOperationException("The Azure Storage resource is already configured with RunAsExisting.");
        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            builder.Resource.RunEndpoints = endpoints;
            AzureStorageExistingInitializer.Register(builder.ApplicationBuilder, builder);
        }
        return builder;
    }

    private static StorageRunEndpoints Endpoints(string account, string suffix)
    {
        ReferenceExpression Url(string service) { var url = $"https://{account}.{service}.{suffix}/"; return ReferenceExpression.Create($"{url}"); }
        return new(Url("blob"), Url("queue"), Url("table"), Url("dfs"));
    }

    /// <summary>Splits an account name, a host (<c>acct.blob.core.windows.net</c>) or a URL into the account name and the endpoint suffix (<c>core.windows.net</c>).</summary>
    internal static (string Account, string Suffix) ParseAccount(string value)
    {
        var host = value.Contains("://", StringComparison.Ordinal) ? new Uri(value).Host : value.Trim().TrimEnd('/');
        var labels = host.Split('.');
        // acct.<service>.<suffix...>: keep the cloud's suffix (core.windows.net, core.chinacloudapi.cn, ...).
        return labels.Length >= 4 ? (labels[0], string.Join('.', labels.Skip(2))) : (labels[0], "core.windows.net");
    }

    /// <summary>Overrides the storage SKU (default Standard_GRS).</summary>
    public static IResourceBuilder<AzureStorageResource> WithSku(this IResourceBuilder<AzureStorageResource> builder, string sku)
    {
        builder.Resource.Sku = sku;
        return builder;
    }

    private const string SkipApiVersionCheckArgument = "--skipApiVersionCheck";

    /// <summary>
    /// Runs Azurite locally instead of connecting to the Pulumi-provisioned account. No effect in publish mode.
    /// Mirrors <c>RunAsEmulator</c>: the container uses the <c>azure-storage/azurite</c> image, serves path-style URLs and skips
    /// the API version check (see <see cref="WithApiVersionCheck"/>).
    /// </summary>
    /// <param name="builder">The Azure Storage resource builder.</param>
    /// <param name="configureContainer">Callback that exposes the emulator container for customization (<c>WithDataVolume</c>, <c>WithBlobPort</c>, ...).</param>
    public static IResourceBuilder<AzureStorageResource> RunAsEmulator(
        this IResourceBuilder<AzureStorageResource> builder,
        Action<IResourceBuilder<AzureStorageEmulatorResource>>? configureContainer = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Resource.Emulator is not null)
            throw new InvalidOperationException("The Azure Storage resource is already configured to run as an emulator.");
        if (builder.Resource.RunEndpoints is not null)
            throw new InvalidOperationException("The Azure Storage resource is already configured with RunAsExisting.");
        if (builder.ApplicationBuilder.ExecutionContext.IsPublishMode) return builder;

        var emulator = new AzureStorageEmulatorResource(builder.Resource);
        // The default arguments come from Azurite's Dockerfile and have to be repeated to be able to add --skipApiVersionCheck;
        // --disableProductStyleUrl makes the emulator use path-style URLs rather than ones with the account name in the host name.
        var emulatorBuilder = builder.ApplicationBuilder.AddResource(emulator)
            .WithImage("azure-storage/azurite", "3.35.0")
            .WithImageRegistry("mcr.microsoft.com")
            .WithHttpEndpoint(targetPort: 10000, name: "blob")
            .WithHttpEndpoint(targetPort: 10001, name: "queue")
            .WithHttpEndpoint(targetPort: 10002, name: "table")
            .WithArgs("azurite", "-l", "/data", "--blobHost", "0.0.0.0", "--queueHost", "0.0.0.0", "--tableHost", "0.0.0.0",
                "--disableProductStyleUrl", SkipApiVersionCheckArgument)
            .WithParentRelationship(builder)
            .ExcludeFromManifest();

        builder.Resource.Emulator = emulator;
        AzureStorageEmulatorInitializer.Register(builder.ApplicationBuilder, builder, emulator, emulatorBuilder);
        configureContainer?.Invoke(emulatorBuilder);
        return builder;
    }

    /// <summary>Adds a bind mount for the data folder to the Azure Storage emulator.</summary>
    /// <param name="builder">The builder for the <see cref="AzureStorageEmulatorResource"/>.</param>
    /// <param name="path">Path relative to the AppHost where emulator storage is persisted between runs. Defaults to <c>.azurite/{storage name}</c>.</param>
    /// <param name="isReadOnly">Whether this is a read-only mount.</param>
    public static IResourceBuilder<AzureStorageEmulatorResource> WithDataBindMount(
        this IResourceBuilder<AzureStorageEmulatorResource> builder, string? path = null, bool isReadOnly = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithBindMount(path ?? $".azurite/{builder.Resource.InnerResource.Name}", "/data", isReadOnly);
    }

    /// <summary>Adds a named volume for the data folder to the Azure Storage emulator.</summary>
    /// <param name="builder">The builder for the <see cref="AzureStorageEmulatorResource"/>.</param>
    /// <param name="name">The volume name. Defaults to a generated name based on the application and storage resource names.</param>
    /// <param name="isReadOnly">Whether this is a read-only volume.</param>
    public static IResourceBuilder<AzureStorageEmulatorResource> WithDataVolume(
        this IResourceBuilder<AzureStorageEmulatorResource> builder, string? name = null, bool isReadOnly = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithVolume(
            name ?? VolumeNameGenerator.Generate(builder.ApplicationBuilder.CreateResourceBuilder(builder.Resource.InnerResource), "data"),
            "/data", isReadOnly);
    }

    /// <summary>Sets the host port the emulator listens on for blob requests.</summary>
    public static IResourceBuilder<AzureStorageEmulatorResource> WithBlobPort(this IResourceBuilder<AzureStorageEmulatorResource> builder, int port)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithEndpoint("blob", endpoint => endpoint.Port = port);
    }

    /// <summary>Sets the host port the emulator listens on for queue requests.</summary>
    public static IResourceBuilder<AzureStorageEmulatorResource> WithQueuePort(this IResourceBuilder<AzureStorageEmulatorResource> builder, int port)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithEndpoint("queue", endpoint => endpoint.Port = port);
    }

    /// <summary>Sets the host port the emulator listens on for table requests.</summary>
    public static IResourceBuilder<AzureStorageEmulatorResource> WithTablePort(this IResourceBuilder<AzureStorageEmulatorResource> builder, int port)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithEndpoint("table", endpoint => endpoint.Port = port);
    }

    /// <summary>
    /// Makes the emulator check that the requested API version is valid. The emulator skips the check by default (like the official
    /// integration), because newer Azure.Storage SDKs otherwise get "API version not supported".
    /// </summary>
    /// <param name="builder">The builder for the <see cref="AzureStorageEmulatorResource"/>.</param>
    /// <param name="enable"><c>true</c> (default) to check the API version, <c>false</c> to skip the check again.</param>
    /// <remarks>
    /// Unlike the official integration, whose implementation adds <c>--skipApiVersionCheck</c> when <paramref name="enable"/> is
    /// <c>true</c> (contradicting its documentation), <c>WithApiVersionCheck()</c> here really enables the check.
    /// </remarks>
    public static IResourceBuilder<AzureStorageEmulatorResource> WithApiVersionCheck(
        this IResourceBuilder<AzureStorageEmulatorResource> builder, bool enable = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithArgs(context =>
        {
            context.Args.Remove(SkipApiVersionCheckArgument);
            if (!enable) context.Args.Add(SkipApiVersionCheckArgument);
            return Task.CompletedTask;
        });
    }

    /// <summary>Adds the blob service of the storage account.</summary>
    public static IResourceBuilder<AzureBlobStorageResource> AddBlobs(this IResourceBuilder<AzureStorageResource> builder, string name)
    {
        var resource = new AzureBlobStorageResource(name, builder.Resource);
        builder.Resource.BlobServices.Add(resource);
        return builder.ApplicationBuilder.AddResource(resource).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>Adds the queue service of the storage account.</summary>
    public static IResourceBuilder<AzureQueueStorageResource> AddQueues(this IResourceBuilder<AzureStorageResource> builder, string name)
    {
        var resource = new AzureQueueStorageResource(name, builder.Resource);
        builder.Resource.QueueServices.Add(resource);
        return builder.ApplicationBuilder.AddResource(resource).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>Adds the table service of the storage account.</summary>
    public static IResourceBuilder<AzureTableStorageResource> AddTables(this IResourceBuilder<AzureStorageResource> builder, string name)
    {
        var resource = new AzureTableStorageResource(name, builder.Resource);
        builder.Resource.TableServices.Add(resource);
        return builder.ApplicationBuilder.AddResource(resource).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>Adds a blob container to the storage account (<paramref name="blobContainerName"/> defaults to <paramref name="name"/>).</summary>
    public static IResourceBuilder<AzureBlobStorageContainerResource> AddBlobContainer(
        this IResourceBuilder<AzureStorageResource> builder, string name, string? blobContainerName = null)
    {
        var resource = new AzureBlobStorageContainerResource(name, blobContainerName ?? name, builder.Resource);
        builder.Resource.BlobContainers.Add(resource);
        return builder.ApplicationBuilder.AddResource(resource).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>Adds a blob container to the storage account through its blob service.</summary>
    public static IResourceBuilder<AzureBlobStorageContainerResource> AddBlobContainer(
        this IResourceBuilder<AzureBlobStorageResource> builder, string name, string? blobContainerName = null)
    {
        var resource = new AzureBlobStorageContainerResource(name, blobContainerName ?? name, builder.Resource.Parent);
        builder.Resource.Parent.BlobContainers.Add(resource);
        return builder.ApplicationBuilder.AddResource(resource).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>Adds a queue to the storage account (<paramref name="queueName"/> defaults to <paramref name="name"/>).</summary>
    public static IResourceBuilder<AzureQueueStorageQueueResource> AddQueue(
        this IResourceBuilder<AzureStorageResource> builder, string name, string? queueName = null)
    {
        var resource = new AzureQueueStorageQueueResource(name, queueName ?? name, builder.Resource);
        builder.Resource.Queues.Add(resource);
        return builder.ApplicationBuilder.AddResource(resource).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>Adds a queue to the storage account through its queue service.</summary>
    public static IResourceBuilder<AzureQueueStorageQueueResource> AddQueue(
        this IResourceBuilder<AzureQueueStorageResource> builder, string name, string? queueName = null)
    {
        var resource = new AzureQueueStorageQueueResource(name, queueName ?? name, builder.Resource.Parent);
        builder.Resource.Parent.Queues.Add(resource);
        return builder.ApplicationBuilder.AddResource(resource).WithParentRelationship(builder).ExcludeFromManifest();
    }
}
