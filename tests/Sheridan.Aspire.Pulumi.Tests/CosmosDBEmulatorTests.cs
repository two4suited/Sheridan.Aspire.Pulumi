using System.Net;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging.Abstractions;
using Sheridan.Aspire.Pulumi.Azure.CosmosDB;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

public sealed class CosmosDBEmulatorTests
{
    [Fact]
    public void WithDataExplorer_adds_a_Data_Explorer_url_to_the_emulator_and_the_cosmos_resource_and_hides_the_health_url()
    {
        var builder = DistributedApplication.CreateBuilder();
        var cosmos = builder.AddPulumiAzureCosmosDB("cosmos");
        AzureCosmosDBEmulatorResource? emulator = null;
        cosmos.RunAsEmulator(e => { e.WithDataExplorer(); emulator = e.Resource; });

        Assert.NotNull(emulator);
        Assert.Contains(emulator.Annotations.OfType<ResourceUrlsCallbackAnnotation>(), _ => true);
        Assert.Contains(cosmos.Resource.Annotations.OfType<ResourceUrlsCallbackAnnotation>(), _ => true);
    }

    [Fact]
    public void Authorization_signs_the_documented_string_to_sign()
    {
        // Key "dGVzdA==" ("test"); string-to-sign: "post\ncolls\ndbs/db\nthu, 01 jan 2026 00:00:00 gmt\n\n".
        var header = CosmosDBEmulatorInitializer.Authorization("POST", "colls", "dbs/db", "Thu, 01 Jan 2026 00:00:00 GMT", "dGVzdA==");
        Assert.StartsWith("type%3Dmaster%26ver%3D1.0%26sig%3D", header);
        Assert.Equal(header, CosmosDBEmulatorInitializer.Authorization("post", "COLLS", "dbs/db", "thu, 01 jan 2026 00:00:00 gmt", "dGVzdA=="));
    }

    [Fact]
    public async Task CreateAsync_posts_databases_then_containers_with_partition_keys()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var seen = new List<(string Path, string Body)>();
        var authorized = true;
        var server = Task.Run(async () =>
        {
            for (var i = 0; i < 3; i++)
            {
                var ctx = await listener.GetContextAsync();
                using var reader = new StreamReader(ctx.Request.InputStream);
                seen.Add((ctx.Request.Url!.AbsolutePath, await reader.ReadToEndAsync()));
                authorized &= !string.IsNullOrEmpty(ctx.Request.Headers["Authorization"]);
                ctx.Response.StatusCode = i == 0 ? 409 : 201; // an existing database is fine
                ctx.Response.Close();
            }
        });

        var cosmos = new AzureCosmosDBResource("cosmos");
        var db = new AzureCosmosDBDatabaseResource("ordersdb", "ordersdb", cosmos);
        cosmos.Databases.Add(db);
        db.Containers.Add(new AzureCosmosDBContainerResource("products", "products", ["/tenant", "/id"], db));
        db.Containers.Add(new AzureCosmosDBContainerResource("orders", "orders", ["/id"], db));
        // Only 3 requests are served: database + first container + second container is 3.
        await CosmosDBEmulatorInitializer.CreateAsync(new Uri($"http://localhost:{port}/"), cosmos, NullLogger.Instance, CancellationToken.None);
        await server;
        Assert.True(authorized);

        Assert.Equal("/dbs", seen[0].Path);
        Assert.Equal("ordersdb", JsonDocument.Parse(seen[0].Body).RootElement.GetProperty("id").GetString());
        Assert.Equal("/dbs/ordersdb/colls", seen[1].Path);
        var pk = JsonDocument.Parse(seen[1].Body).RootElement.GetProperty("partitionKey");
        Assert.Equal("MultiHash", pk.GetProperty("kind").GetString());
        Assert.Equal(2, pk.GetProperty("paths").GetArrayLength());
        Assert.Equal("Hash", JsonDocument.Parse(seen[2].Body).RootElement.GetProperty("partitionKey").GetProperty("kind").GetString());
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
