using Sheridan.Aspire.Pulumi.Azure.Base;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

public sealed class HideInRunModeTests
{
    private static IDistributedApplicationBuilder PublishBuilder() =>
        DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = ["--operation", "publish", "--output-path", Path.GetTempPath()] });

    private static bool IsHidden(IResource r) =>
        r.TryGetLastAnnotation<ResourceSnapshotAnnotation>(out var a) && a.InitialSnapshot.IsHidden;

    [Fact]
    public void Deploy_only_resources_are_hidden_in_run_mode()
    {
        var builder = DistributedApplication.CreateBuilder();
        var stack = builder.AddPulumiStack("app");
        var rg = builder.AddPulumiAzureResourceGroup("rg-app");
        var acr = builder.AddPulumiAzureContainerRegistry("acr").AsExisting("existing", "rg");
        var aca = builder.AddPulumiAzureContainerAppEnvironment("aca").AsExisting("existing", "rg");

        Assert.All(new IResource[] { stack.Resource, rg.Resource, acr.Resource, aca.Resource }, r => Assert.True(IsHidden(r), r.Name));
    }

    [Fact]
    public void Resources_stay_visible_in_publish_mode()
    {
        var builder = PublishBuilder();
        var stack = builder.AddPulumiStack("app");
        var rg = builder.AddPulumiAzureResourceGroup("rg-app");

        Assert.False(IsHidden(stack.Resource));
        Assert.False(IsHidden(rg.Resource));
    }
}
