using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure.AppContainers;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

/// <summary>Local-image mode (issue #213): tag locally, never push or log in; plus a caller-supplied image tag.</summary>
public sealed class ContainerAppLocalImagesTests
{
    private static IResourceBuilder<AzureContainerAppEnvironmentResource> Env()
    {
        var builder = DistributedApplication.CreateBuilder(["--publisher", "manifest", "--output-path", Path.GetTempPath()]);
        return builder.AddPulumiAzureContainerAppEnvironment("aca-env");
    }

    [Fact]
    public void Defaults_push_to_the_registry_with_the_deploy_tag()
    {
        var env = Env().Resource;
        Assert.False(env.LocalImages);
        Assert.Null(env.ImageTag);
    }

    [Fact]
    public async Task WithLocalImages_makes_the_registry_endpoint_empty_so_aspire_only_tags()
    {
        var env = Env().WithLocalImages();

        Assert.True(env.Resource.LocalImages);
        // Aspire's push step tags without pushing when the registry endpoint is empty.
        var endpoint = await ((IContainerRegistry)env.Resource).Endpoint.GetValueAsync(CancellationToken.None);
        Assert.True(string.IsNullOrEmpty(endpoint));
    }

    [Fact]
    public void WithImageTag_sets_a_stable_tag_and_rejects_blank()
    {
        var env = Env().WithImageTag("dev");
        Assert.Equal("dev", env.Resource.ImageTag);
        Assert.Throws<ArgumentException>(() => env.WithImageTag(" "));
    }
}
