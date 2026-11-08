using Sheridan.Aspire.Pulumi.Azure.ContainerRegistry;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

public sealed class ContainerRegistryPurgeTests
{
    [Fact]
    public void Purge_task_also_removes_untagged_manifests()
    {
        var content = AzureContainerRegistryResource.BuildPurgeTaskContent(
            new AzureContainerRegistryPurgeTask("0 1 * * *", "api:.*", "7d", 5));

        Assert.Equal(
            "version: v1.1.0\nsteps:\n- cmd: mcr.microsoft.com/acr/acr-cli:0.21 purge --filter 'api:.*' --ago 7d --min-tags 5 --untagged --max-untagged-manifests 0",
            content);
    }
}
