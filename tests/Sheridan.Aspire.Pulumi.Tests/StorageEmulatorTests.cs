using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure.Storage;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

public sealed class StorageEmulatorTests
{
    [Fact]
    public async Task RunAsEmulator_starts_azurite_with_skipApiVersionCheck()
    {
        var builder = DistributedApplication.CreateBuilder();
        var storage = builder.AddPulumiStorage("storage").RunAsEmulator();

        var emulator = storage.Resource.Emulator!;
        var args = new List<object>();
        var context = new CommandLineArgsCallbackContext(args);
        foreach (var annotation in emulator.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
            await annotation.Callback(context);

        Assert.Contains("--skipApiVersionCheck", args.Select(a => a.ToString()));
        // Still listens on all interfaces, as the image default does.
        Assert.Contains("--tableHost", args.Select(a => a.ToString()));
    }
}
