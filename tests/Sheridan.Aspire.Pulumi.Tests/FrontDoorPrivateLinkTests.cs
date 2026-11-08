using Aspire.Hosting;
using Sheridan.Aspire.Pulumi.Azure.AppService;
using Sheridan.Aspire.Pulumi.Azure.Base;
using Sheridan.Aspire.Pulumi.Azure.FrontDoor;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

public sealed class FrontDoorPrivateLinkTests
{
    [Fact]
    public void App_service_environment_gets_one_private_link_annotation_per_front_door()
    {
        var builder = DistributedApplication.CreateBuilder();
        var env = builder.AddPulumiAzureAppServiceEnvironment("env");
        var fd = builder.AddPulumiFrontDoor("fd");

        env.WithAzureFrontDoorPrivateLink(fd).WithAzureFrontDoorPrivateLink(fd);

        var annotation = Assert.Single(env.Resource.Annotations.OfType<PulumiPrivateLinkTargetAnnotation>());
        Assert.Same(fd.Resource, annotation.Source);
    }
}
