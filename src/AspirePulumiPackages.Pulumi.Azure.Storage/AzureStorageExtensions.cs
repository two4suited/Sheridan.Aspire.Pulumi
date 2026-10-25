using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace AspirePulumiPackages.Pulumi.Azure.Storage;

/// <summary>Pulumi equivalents of the Aspire.Hosting.Azure.Storage extension methods.</summary>
public static class AzureStorageExtensions
{
    /// <summary>Adds an Azure Storage account provisioned by the Pulumi stack (the equivalent of AddAzureStorage).</summary>
    public static IResourceBuilder<AzureStorageResource> AddPulumiStorage(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddResource(new AzureStorageResource(name)).ExcludeFromManifest();

    /// <summary>Alias of <see cref="AddPulumiStorage"/> following the <c>AddPulumiAzureX</c> naming used by the other Azure integrations.</summary>
    public static IResourceBuilder<AzureStorageResource> AddPulumiAzureStorage(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiStorage(name);

    /// <summary>Overrides the storage SKU (default Standard_GRS).</summary>
    public static IResourceBuilder<AzureStorageResource> WithSku(this IResourceBuilder<AzureStorageResource> builder, string sku)
    {
        builder.Resource.Sku = sku;
        return builder;
    }

    /// <summary>
    /// Runs Azurite locally instead of connecting to the Pulumi-provisioned account. No effect in publish mode.
    /// </summary>
    public static IResourceBuilder<AzureStorageResource> RunAsEmulator(this IResourceBuilder<AzureStorageResource> builder)
    {
        if (builder.ApplicationBuilder.ExecutionContext.IsPublishMode) return builder;

        var emulator = new AzureStorageEmulatorResource(builder.Resource);
        builder.ApplicationBuilder.AddResource(emulator)
            .WithImage("azure-storage/azurite", "3.35.0")
            .WithImageRegistry("mcr.microsoft.com")
            .WithHttpEndpoint(targetPort: 10000, name: "blob")
            .WithHttpEndpoint(targetPort: 10001, name: "queue")
            .WithHttpEndpoint(targetPort: 10002, name: "table")
            .WithParentRelationship(builder)
            .ExcludeFromManifest();

        builder.Resource.Emulator = emulator;
        return builder;
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
