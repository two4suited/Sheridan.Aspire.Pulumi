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

        Assert.Contains("acr purge --filter 'api:.*' --ago 7d --keep 5", content);
        Assert.Contains("--untagged --max-untagged-manifests 0", content);
    }
}
