using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi;
using Sheridan.Aspire.Pulumi.Azure.Base;
using Sheridan.Aspire.Pulumi.Azure.Storage;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

public sealed class StorageExistingTests
{
    private static Dictionary<string, object?> Invoke(PulumiProgram p, string id) =>
        (Dictionary<string, object?>)((Dictionary<string, object?>)p.Variables[id]!)["fn::invoke"]!;

    private static object? Arg(Dictionary<string, object?> invoke, string key) =>
        ((Dictionary<string, object?>)invoke["arguments"]!)[key];

    [Fact]
    public void AsExisting_and_PublishAsExisting_record_account_and_group()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = builder.AddPulumiStorage("storage").AsExisting("sharedacct", "shared-rg");
        Assert.Equal("sharedacct", storage.Resource.ExistingAccountName);
        Assert.Equal("shared-rg", storage.Resource.ExistingAccountResourceGroup);
        Assert.Equal("shared-rg", storage.Resource.ExistingResourceGroup);

        var other = builder.AddPulumiStorage("storage2").PublishAsExisting("acct2");
        Assert.Equal("acct2", other.Resource.ExistingAccountName);
        Assert.Null(other.Resource.ExistingResourceGroup);

        Assert.Null(builder.AddPulumiStorage("storage3").Resource.ExistingAccountName);
        Assert.Throws<ArgumentException>(() => builder.AddPulumiStorage("s4").AsExisting(""));
        Assert.Throws<ArgumentException>(() => builder.AddPulumiStorage("s5").AsExisting("acct", ""));
    }

    [Fact]
    public void Lookup_is_emitted_under_the_accounts_id_and_uses_the_accounts_group()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = builder.AddPulumiStorage("storage").AsExisting("sharedacct", "shared-rg");
        var program = new PulumiProgram();
        var rg = storage.Resource.AddExistingLookup(program, "${otherRg.name}");

        Assert.Equal("shared-rg", rg);
        var invoke = Invoke(program, storage.Resource.Id);
        Assert.Equal("azure-native:storage:getStorageAccount", invoke["function"]);
        Assert.Equal("sharedacct", Arg(invoke, "accountName"));
        Assert.Equal("shared-rg", Arg(invoke, "resourceGroupName"));
        Assert.DoesNotContain(storage.Resource.Id, program.Resources.Keys);
    }

    [Fact]
    public void Lookup_without_any_resource_group_throws_at_generation()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = builder.AddPulumiStorage("storage").AsExisting("sharedacct");
        Assert.Throws<InvalidOperationException>(() => storage.Resource.AddExistingLookup(new PulumiProgram(), "${x.name}"));

        var withGroup = builder.AddPulumiStorage("s2").AsExisting("sharedacct").WithExistingResourceGroup("rg-x");
        Assert.Equal("rg-x", withGroup.Resource.AddExistingLookup(new PulumiProgram(), "rg-x"));
    }

    [Fact]
    public void RunAsExisting_with_an_account_name_derives_every_service_endpoint_without_a_stack()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = builder.AddPulumiStorage("storage").AsExisting("acct", "rg").RunAsExisting("acct");
        var blobs = storage.AddBlobs("blobs");
        var queues = storage.AddQueues("queues");
        var tables = storage.AddTables("tables");
        var container = storage.AddBlobContainer("uploads");
        var queue = storage.AddQueue("jobs");

        Assert.Equal("https://acct.blob.core.windows.net/", blobs.Resource.ConnectionStringExpression.ValueExpression);
        Assert.Equal("https://acct.queue.core.windows.net/", queues.Resource.ConnectionStringExpression.ValueExpression);
        Assert.Equal("https://acct.table.core.windows.net/", tables.Resource.ConnectionStringExpression.ValueExpression);
        Assert.Equal("Endpoint=https://acct.blob.core.windows.net/;ContainerName=uploads;", container.Resource.ConnectionStringExpression.ValueExpression);
        Assert.Equal("Endpoint=https://acct.queue.core.windows.net/;QueueName=jobs;", queue.Resource.ConnectionStringExpression.ValueExpression);
    }

    [Theory]
    [InlineData("acct", "acct", "core.windows.net")]
    [InlineData("acct.blob.core.windows.net", "acct", "core.windows.net")]
    [InlineData("https://acct.queue.core.windows.net/", "acct", "core.windows.net")]
    [InlineData("https://acct.blob.core.chinacloudapi.cn:443/", "acct", "core.chinacloudapi.cn")]
    [InlineData("Acct.privatelink.blob.core.windows.net", "acct", "core.windows.net")]
    [InlineData("acct.dfs.core.usgovcloudapi.net", "acct", "core.usgovcloudapi.net")]
    public void RunAsExisting_accepts_a_name_host_or_url(string value, string account, string suffix)
    {
        Assert.Equal((account, suffix), AzureStorageExtensions.ParseAccount(value));
    }

    [Fact]
    public void RunAsExisting_accepts_a_parameter()
    {
        var builder = DistributedApplication.CreateBuilder();
        var name = builder.AddParameter("storage-account");
        var storage = builder.AddPulumiStorage("storage").RunAsExisting(name);
        Assert.Equal("https://{storage-account.value}.blob.core.windows.net/", storage.AddBlobs("blobs").Resource.ConnectionStringExpression.ValueExpression);
    }

    [Fact]
    public void RunAsExisting_registers_an_account_health_check_and_conflicts_with_the_emulator()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = builder.AddPulumiStorage("storage").RunAsExisting("acct");
        Assert.Contains(storage.Resource.Annotations.OfType<HealthCheckAnnotation>(), h => h.Key == "storage_check");
        Assert.Throws<InvalidOperationException>(() => storage.RunAsEmulator());
        Assert.Throws<InvalidOperationException>(() => storage.RunAsExisting("other"));

        var emulated = builder.AddPulumiStorage("emulated").RunAsEmulator();
        Assert.Throws<InvalidOperationException>(() => emulated.RunAsExisting("acct"));
    }

    [Fact]
    public void RunAsExisting_has_no_effect_in_publish_mode()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = ["--publisher", "manifest"], DisableDashboard = true });
        var storage = builder.AddPulumiStorage("storage").RunAsExisting("acct");
        Assert.Null(storage.Resource.RunEndpoints);
    }
}
