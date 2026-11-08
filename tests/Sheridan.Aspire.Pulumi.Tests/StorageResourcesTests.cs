using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi;
using Sheridan.Aspire.Pulumi.Azure;
using Sheridan.Aspire.Pulumi.Azure.Base;
using Sheridan.Aspire.Pulumi.Azure.Storage;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

public sealed class StorageResourcesTests
{
    private sealed class FakeEnvironment(string name) : Resource(name), IComputeEnvironmentResource, IPulumiManagedIdentityResource
    {
        public string PrincipalIdExpression => "${" + Name + "Identity.principalId}";
    }

    private static IResourceBuilder<AzureStorageResource> Storage(IDistributedApplicationBuilder builder) =>
        builder.AddPulumiStorage("storage").WithPulumiStack(builder.AddPulumiStack("s", "dev"));

    private static Dictionary<string, object?> Res(PulumiProgram p, string id) => (Dictionary<string, object?>)p.Resources[id]!;

    private static Dictionary<string, object?> Props(PulumiProgram p, string id) => (Dictionary<string, object?>)Res(p, id)["properties"]!;

    private static PulumiProgram Emit(AzureStorageResource storage)
    {
        var program = new PulumiProgram();
        storage.EmitResources(program, "rg", "westus");
        return program;
    }

    [Fact]
    public void AddDataLake_enables_the_hierarchical_namespace_and_exposes_the_dfs_endpoint()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = Storage(builder);
        var lake = storage.AddDataLake("lake");

        Assert.True(storage.Resource.IsHnsEnabled);
        Assert.Contains(lake.Resource, storage.Resource.DataLakeServices);
        var program = Emit(storage.Resource);
        Assert.Equal(true, Props(program, storage.Resource.Id)["isHnsEnabled"]);
        Assert.Equal("${storage.primaryEndpoints.dfs}", program.Outputs["storageDataLakeEndpoint"]);
    }

    [Fact]
    public void Accounts_without_data_lake_keep_the_flat_namespace()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = Storage(builder);
        Assert.Equal(false, Props(Emit(storage.Resource), storage.Resource.Id)["isHnsEnabled"]);
    }

    [Fact]
    public void AddDataLakeFileSystem_provisions_a_blob_container_and_a_default_data_lake_service()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = Storage(builder);
        var fs = storage.AddDataLakeFileSystem("raw", "raw-zone");
        storage.AddDataLakeFileSystem("curated");

        var service = Assert.Single(storage.Resource.DataLakeServices);
        Assert.Equal("storage-data-lake", service.Name);
        Assert.Same(service, fs.Resource.Parent);
        Assert.Equal(2, storage.Resource.DataLakeFileSystems.Count);

        var program = Emit(storage.Resource);
        Assert.Equal("azure-native:storage:BlobServiceProperties", (string?)Res(program, "storageBlobService")["type"]);
        var container = Props(program, "storage" + fs.Resource.Id);
        Assert.Equal("raw-zone", container["containerName"]);
        Assert.Equal("azure-native:storage:BlobContainer", (string?)Res(program, "storage" + fs.Resource.Id)["type"]);
    }

    [Fact]
    public void AddDataLake_keeps_the_first_service_as_the_parent_of_file_systems()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = Storage(builder);
        var lake = storage.AddDataLake("lake");
        var fs = storage.AddDataLakeFileSystem("raw");

        Assert.Same(lake.Resource, fs.Resource.Parent);
        Assert.Single(storage.Resource.DataLakeServices);
    }

    [Fact]
    public void Data_lake_connection_strings_use_the_dfs_endpoint_and_file_system_name()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = Storage(builder).AsExisting("acct", "rg").RunAsExisting("acct");
        var lake = storage.AddDataLake("lake");
        var fs = storage.AddDataLakeFileSystem("raw", "raw-zone");

        Assert.Equal("https://acct.dfs.core.windows.net/", lake.Resource.ConnectionStringExpression.ValueExpression);
        Assert.Equal("Endpoint=https://acct.dfs.core.windows.net/;FileSystemName=raw-zone", fs.Resource.ConnectionStringExpression.ValueExpression);
    }

    [Fact]
    public void The_emulator_does_not_support_data_lake_in_either_order()
    {
        var builder = DistributedApplication.CreateBuilder();
        var lakeFirst = builder.AddPulumiStorage("a");
        lakeFirst.AddDataLake("lake");
        Assert.Throws<InvalidOperationException>(() => lakeFirst.RunAsEmulator());

        var emulatorFirst = builder.AddPulumiStorage("b").RunAsEmulator();
        Assert.Throws<InvalidOperationException>(() => emulatorFirst.AddDataLake("lake"));
        Assert.Throws<InvalidOperationException>(() => emulatorFirst.AddDataLakeFileSystem("fs"));
    }

    [Fact]
    public void Existing_accounts_still_get_their_containers_and_queues_applied()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = Storage(builder).AsExisting("acct", "shared-rg");
        var container = storage.AddBlobContainer("uploads");
        var queue = storage.AddQueue("jobs");

        var program = Emit(storage.Resource);
        Assert.DoesNotContain("storage", program.Resources.Keys);
        Assert.Contains("storage", program.Variables.Keys);
        Assert.Equal("shared-rg", Props(program, "storage" + container.Resource.Id)["resourceGroupName"]);
        Assert.Equal("${storage.name}", Props(program, "storage" + queue.Resource.Id)["accountName"]);
        Assert.Equal("${storage.primaryEndpoints.blob}", program.Outputs["storageBlobEndpoint"]);
    }

    private static (IResourceBuilder<ContainerResource> App, FakeEnvironment Env) HostedApp(
        IDistributedApplicationBuilder builder, string name, FakeEnvironment env)
    {
        var app = builder.AddContainer(name, "img");
        app.WithAnnotation(new DeploymentTargetAnnotation(app.Resource) { ComputeEnvironment = env });
        return (app, env);
    }

    private static string[] Grants(AzureStorageResource storage, params IResource[] apps) =>
        storage.ComputeRoleGrants(apps).Select(kv => $"{kv.Key.Env.Name}:{kv.Value}").Order().ToArray();

    [Fact]
    public void Referencing_the_account_grants_blob_queue_and_table_roles_and_a_child_grants_only_its_own()
    {
        var builder = DistributedApplication.CreateBuilder();
        var env = new FakeEnvironment("env");
        var storage = Storage(builder);
        var blobs = storage.AddBlobs("blobs");
        var queue = storage.AddQueue("jobs");
        var lakeFs = storage.AddDataLakeFileSystem("raw");

        var viaAccount = HostedApp(builder, "a", env).App.WithReference(blobs).WithRelationship(storage.Resource, "Reference");
        var viaQueue = HostedApp(builder, "b", env).App.WithReference(queue);
        var viaLake = HostedApp(builder, "c", new FakeEnvironment("lakeenv")).App.WithReference(lakeFs);

        Assert.Equal(["env:Blob", "env:Queue", "env:Table"], Grants(storage.Resource, viaAccount.Resource));
        Assert.Equal(["env:Queue"], Grants(storage.Resource, viaQueue.Resource));
        Assert.Equal(["lakeenv:Blob"], Grants(storage.Resource, viaLake.Resource));
        Assert.Equal(["env:Blob", "env:Queue", "env:Table"], Grants(storage.Resource, viaAccount.Resource, viaQueue.Resource));
    }

    [Fact]
    public void WithRoleAssignments_replaces_the_default_roles()
    {
        var builder = DistributedApplication.CreateBuilder();
        var env = new FakeEnvironment("env");
        var storage = Storage(builder);
        var blobs = storage.AddBlobs("blobs");
        var queues = storage.AddQueues("queues");

        var reader = HostedApp(builder, "reader", env).App.WithReference(blobs).WithReference(queues)
            .WithRoleAssignments(storage, AzureStorageRole.StorageBlobDataReader, AzureStorageRole.StorageQueueDataMessageSender);

        Assert.Equal(["env:StorageBlobDataReader", "env:StorageQueueDataMessageSender"], Grants(storage.Resource, reader.Resource));
    }

    [Fact]
    public void WithRoleAssignments_with_no_roles_assigns_none_and_never_duplicates_a_role_definition()
    {
        var builder = DistributedApplication.CreateBuilder();
        var env = new FakeEnvironment("env");
        var storage = Storage(builder);
        var blobs = storage.AddBlobs("blobs");

        var none = HostedApp(builder, "none", env).App.WithReference(blobs).WithRoleAssignments(storage);
        Assert.Empty(Grants(storage.Resource, none.Resource));

        // A default Blob grant and a custom Blob Data Contributor grant are the same assignment.
        var custom = HostedApp(builder, "custom", env).App.WithReference(blobs).WithRoleAssignments(storage, AzureStorageRole.StorageBlobDataContributor);
        var plain = HostedApp(builder, "plain", env).App.WithReference(blobs);
        Assert.Equal(["env:Blob"], Grants(storage.Resource, custom.Resource, plain.Resource));
    }

    [Fact]
    public void Role_definitions_are_distinct_and_cover_every_role()
    {
        var ids = Enum.GetValues<AzureStorageRole>().Select(AzureStorageRoles.RoleDefinitionId).ToList();
        Assert.Equal(20, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(AzureStorageResource.BlobDataContributorRoleId, AzureStorageRoles.RoleDefinitionId(AzureStorageRole.StorageBlobDataContributor));
        Assert.Equal(AzureStorageResource.QueueDataContributorRoleId, AzureStorageRoles.RoleDefinitionId(AzureStorageRole.StorageQueueDataContributor));
        Assert.Equal(AzureStorageResource.TableDataContributorRoleId, AzureStorageRoles.RoleDefinitionId(AzureStorageRole.StorageTableDataContributor));
    }

    [Fact]
    public void WithRoleAssignments_rejects_unknown_roles()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = Storage(builder);
        var app = builder.AddContainer("app", "img");
        Assert.Throws<ArgumentException>(() => app.WithRoleAssignments(storage, (AzureStorageRole)999));
    }
}
