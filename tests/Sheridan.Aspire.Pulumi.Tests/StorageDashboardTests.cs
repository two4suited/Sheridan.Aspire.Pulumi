using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sheridan.Aspire.Pulumi.Azure.Storage;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

public sealed class StorageDashboardTests
{
    private static string? Icon(IResource resource) => resource.Annotations.OfType<ResourceIconAnnotation>().SingleOrDefault()?.IconName;

    private static string[] Checks(IResource resource) => resource.Annotations.OfType<HealthCheckAnnotation>().Select(a => a.Key).ToArray();

    [Fact]
    public void Every_storage_resource_has_the_icon_the_official_integration_uses()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = builder.AddPulumiStorage("storage");
        var blobs = storage.AddBlobs("blobs");
        var queues = storage.AddQueues("queues");
        var tables = storage.AddTables("tables");
        var container = storage.AddBlobContainer("uploads");
        var viaBlobs = blobs.AddBlobContainer("images");
        var queue = storage.AddQueue("jobs");
        var viaQueues = queues.AddQueue("mail");
        var lake = storage.AddDataLake("lake");
        var fs = storage.AddDataLakeFileSystem("raw");

        Assert.Equal("HardDrive", Icon(storage.Resource));
        Assert.Equal("DocumentMultiple", Icon(blobs.Resource));
        Assert.Equal("Mail", Icon(queues.Resource));
        Assert.Equal("TableSimple", Icon(tables.Resource));
        Assert.Equal("HardDrive", Icon(lake.Resource));
        Assert.All(new IResource[] { container.Resource, viaBlobs.Resource, fs.Resource }, r => Assert.Equal("FolderOpen", Icon(r)));
        Assert.All(new IResource[] { queue.Resource, viaQueues.Resource }, r => Assert.Equal("Mail", Icon(r)));
    }

    [Fact]
    public void Blob_and_queue_resources_get_health_checks_but_tables_and_data_lake_do_not()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = builder.AddPulumiStorage("storage");
        var blobs = storage.AddBlobs("blobs");
        var queues = storage.AddQueues("queues");
        var tables = storage.AddTables("tables");
        var container = storage.AddBlobContainer("uploads");
        var queue = storage.AddQueue("jobs");
        var viaBlobs = blobs.AddBlobContainer("images");
        var viaQueues = queues.AddQueue("mail");

        Assert.Equal(["blobs_check"], Checks(blobs.Resource));
        Assert.Equal(["queues_check"], Checks(queues.Resource));
        Assert.Equal(["uploads_check"], Checks(container.Resource));
        Assert.Equal(["jobs_check"], Checks(queue.Resource));
        Assert.Equal(["images_check"], Checks(viaBlobs.Resource));
        Assert.Equal(["mail_check"], Checks(viaQueues.Resource));
        Assert.Empty(Checks(tables.Resource));
        // A plain (deployed) account has no local endpoint to probe.
        Assert.Empty(Checks(storage.Resource));

        var registered = builder.Services.BuildServiceProvider()
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>().Value.Registrations.Select(r => r.Name);
        Assert.Subset(registered.ToHashSet(), new HashSet<string> { "blobs_check", "queues_check", "uploads_check", "jobs_check", "images_check", "mail_check" });
    }

    [Fact]
    public void The_account_check_is_added_for_the_emulator_and_for_RunAsExisting()
    {
        var builder = DistributedApplication.CreateBuilder();
        Assert.Equal(["storage_check"], Checks(builder.AddPulumiStorage("storage").RunAsEmulator().Resource));
        Assert.Equal(["other_check"], Checks(builder.AddPulumiStorage("other").RunAsExisting("acct").Resource));
    }
}
