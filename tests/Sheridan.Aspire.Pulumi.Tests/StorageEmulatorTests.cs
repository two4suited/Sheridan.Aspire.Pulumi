using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure.Storage;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

public sealed class StorageEmulatorTests
{
    private static async Task<List<string>> ArgsAsync(IResource resource)
    {
        var args = new List<object>();
        foreach (var callback in resource.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
            await callback.Callback(new CommandLineArgsCallbackContext(args, resource));
        return args.Select(a => a.ToString()!).ToList();
    }

    [Fact]
    public async Task RunAsEmulator_uses_the_upstream_azurite_arguments_and_image()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = builder.AddPulumiStorage("storage").RunAsEmulator();
        var emulator = storage.Resource.Emulator!;

        Assert.Equal(
            ["azurite", "-l", "/data", "--blobHost", "0.0.0.0", "--queueHost", "0.0.0.0", "--tableHost", "0.0.0.0",
             "--disableProductStyleUrl", "--skipApiVersionCheck"],
            await ArgsAsync(emulator));
        var image = emulator.Annotations.OfType<ContainerImageAnnotation>().Single();
        Assert.Equal("mcr.microsoft.com", image.Registry);
        Assert.Equal("azure-storage/azurite", image.Image);
        Assert.Equal("3.35.0", image.Tag);
    }

    [Fact]
    public void RunAsEmulator_twice_throws()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = builder.AddPulumiStorage("storage").RunAsEmulator();
        Assert.Throws<InvalidOperationException>(() => storage.RunAsEmulator());
    }

    [Fact]
    public async Task WithApiVersionCheck_toggles_skipApiVersionCheck()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = builder.AddPulumiStorage("storage").RunAsEmulator(c => c.WithApiVersionCheck());
        Assert.DoesNotContain("--skipApiVersionCheck", await ArgsAsync(storage.Resource.Emulator!));

        var other = builder.AddPulumiStorage("other").RunAsEmulator(c => c.WithApiVersionCheck().WithApiVersionCheck(false));
        Assert.Single(await ArgsAsync(other.Resource.Emulator!), "--skipApiVersionCheck");
    }

    [Fact]
    public void ConfigureContainer_can_set_ports_and_data_mounts()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = builder.AddPulumiStorage("storage").RunAsEmulator(c => c
            .WithBlobPort(11000).WithQueuePort(11001).WithTablePort(11002)
            .WithDataVolume("azurite-data")
            .WithDataBindMount("custom"));
        var emulator = storage.Resource.Emulator!;

        var endpoints = emulator.Annotations.OfType<EndpointAnnotation>().ToDictionary(e => e.Name);
        Assert.Equal(11000, endpoints["blob"].Port);
        Assert.Equal(11001, endpoints["queue"].Port);
        Assert.Equal(11002, endpoints["table"].Port);
        Assert.Equal(10000, endpoints["blob"].TargetPort);

        var mounts = emulator.Annotations.OfType<ContainerMountAnnotation>().ToList();
        Assert.Contains(mounts, m => m.Type == ContainerMountType.Volume && m.Source == "azurite-data" && m.Target == "/data");
        Assert.Contains(mounts, m => m.Type == ContainerMountType.BindMount && m.Target == "/data");
    }

    [Fact]
    public void Default_data_mount_names_follow_the_storage_resource_name()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = builder.AddPulumiStorage("files").RunAsEmulator(c => c.WithDataVolume().WithDataBindMount());
        var mounts = storage.Resource.Emulator!.Annotations.OfType<ContainerMountAnnotation>().ToList();

        Assert.EndsWith("-files-data", mounts.Single(m => m.Type == ContainerMountType.Volume).Source);
        Assert.EndsWith(".azurite/files", mounts.Single(m => m.Type == ContainerMountType.BindMount).Source!.Replace('\\', '/'));
    }

    [Fact]
    public void RunAsEmulator_is_a_no_op_in_publish_mode()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = ["--publisher", "manifest"], DisableDashboard = true });
        var storage = builder.AddPulumiStorage("storage").RunAsEmulator();
        Assert.Null(storage.Resource.Emulator);
    }
}
