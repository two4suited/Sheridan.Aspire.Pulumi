using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure.Storage;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

public sealed class StorageEmulatorInitializerTests
{
    [Fact]
    public async Task CreateAsync_creates_every_declared_container_and_queue_including_those_added_through_the_services()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var seen = new List<(string Method, string Path, string Query)>();
        var server = Task.Run(async () =>
        {
            for (var i = 0; i < 4; i++)
            {
                var ctx = await listener.GetContextAsync();
                seen.Add((ctx.Request.HttpMethod, ctx.Request.Url!.AbsolutePath, ctx.Request.Url.Query));
                ctx.Response.StatusCode = 201;
                ctx.Response.Headers["ETag"] = "\"0x1\"";
                ctx.Response.Headers["Last-Modified"] = "Thu, 01 Jan 2026 00:00:00 GMT";
                ctx.Response.Close();
            }
        });

        var builder = DistributedApplication.CreateBuilder();
        var storage = builder.AddPulumiStorage("storage");
        storage.AddBlobContainer("uploads");
        storage.AddBlobs("blobs").AddBlobContainer("images", "img");
        storage.AddQueue("jobs");
        storage.AddQueues("queues").AddQueue("mail", "outgoing");

        var key = AzureStorageEmulatorResource.AccountKey;
        var blobs = new global::Azure.Storage.Blobs.BlobServiceClient($"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={key};BlobEndpoint=http://localhost:{port}/devstoreaccount1;");
        var queues = new global::Azure.Storage.Queues.QueueServiceClient($"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={key};QueueEndpoint=http://localhost:{port}/devstoreaccount1;");
        await AzureStorageEmulatorInitializer.CreateAsync(blobs, queues, storage.Resource, CancellationToken.None);
        await server;

        Assert.All(seen, r => Assert.Equal("PUT", r.Method));
        Assert.Equal(
            ["/devstoreaccount1/uploads", "/devstoreaccount1/img", "/devstoreaccount1/jobs", "/devstoreaccount1/outgoing"],
            seen.Select(r => r.Path).ToArray());
        Assert.All(seen.Take(2), r => Assert.Contains("restype=container", r.Query));
    }

    [Fact]
    public void Resources_referencing_the_storage_or_a_child_wait_for_the_account_but_the_emulator_does_not()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = builder.AddPulumiStorage("storage").RunAsEmulator();
        var blobs = storage.AddBlobs("blobs");
        var queue = storage.AddQueue("jobs");
        var viaService = builder.AddContainer("a", "img").WithReference(blobs);
        var viaQueue = builder.AddContainer("b", "img").WithReference(queue);
        var unrelated = builder.AddContainer("c", "img");

        storage.Resource.AddWaitForReferencing(new DistributedApplicationModel(builder.Resources), storage.Resource.Emulator!);

        Assert.Contains(viaService.Resource.Annotations.OfType<WaitAnnotation>(), w => w.Resource == storage.Resource);
        Assert.Contains(viaQueue.Resource.Annotations.OfType<WaitAnnotation>(), w => w.Resource == storage.Resource);
        Assert.Empty(unrelated.Resource.Annotations.OfType<WaitAnnotation>());
        Assert.Empty(storage.Resource.Emulator!.Annotations.OfType<WaitAnnotation>());
    }

    [Fact]
    public void The_emulator_container_gets_a_health_check()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = builder.AddPulumiStorage("storage").RunAsEmulator();
        Assert.Contains(storage.Resource.Emulator!.Annotations.OfType<HealthCheckAnnotation>(), h => h.Key == "storage-emulator_check");
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
