using Aspire.Hosting;
using Sheridan.Aspire.Pulumi.Azure.Kubernetes;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

public sealed class KubernetesExistingTests
{
    [Fact]
    public void New_clusters_are_not_existing()
    {
        Assert.False(new AzureKubernetesEnvironmentResource("aks").IsExisting);
    }

    [Fact]
    public void AsExisting_and_PublishAsExisting_record_cluster_and_group()
    {
        var builder = DistributedApplication.CreateBuilder();
        var aks = builder.AddPulumiKubernetes("aks").AsExisting("shared", "platform-rg");
        Assert.True(aks.Resource.IsExisting);
        Assert.Equal("shared", aks.Resource.ExistingClusterName);
        Assert.Equal("platform-rg", aks.Resource.ExistingClusterResourceGroup);

        var other = builder.AddPulumiKubernetes("aks2").PublishAsExisting("c2", null);
        Assert.Equal("c2", other.Resource.ExistingClusterName);
        Assert.Null(other.Resource.ExistingClusterResourceGroup);
    }

    [Fact]
    public void AsExisting_rejects_an_empty_name()
    {
        var builder = DistributedApplication.CreateBuilder();
        Assert.Throws<ArgumentException>(() => builder.AddPulumiKubernetes("aks").AsExisting("", null));
    }
}
