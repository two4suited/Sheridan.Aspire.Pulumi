using Aspire.Hosting;
using Sheridan.Aspire.Pulumi.Azure.AppContainers;
using Sheridan.Aspire.Pulumi.Azure.ContainerRegistry;
using Sheridan.Aspire.Pulumi.Azure.ServiceBus;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

public sealed class ExistingInfrastructureTests
{
    [Fact]
    public void New_resources_are_not_existing()
    {
        Assert.Null(new AzureServiceBusResource("sb").ExistingNamespaceName);
        Assert.Null(new AzureContainerRegistryResource("acr").ExistingRegistryName);
        Assert.Null(new AzureContainerAppEnvironmentResource("env").ExistingEnvironmentName);
    }

    [Fact]
    public void ServiceBus_AsExisting_and_PublishAsExisting_record_namespace_and_group()
    {
        var builder = DistributedApplication.CreateBuilder();
        var sb = builder.AddPulumiServiceBus("sb").AsExisting("shared-ns", "shared-rg");
        Assert.Equal("shared-ns", sb.Resource.ExistingNamespaceName);
        Assert.Equal("shared-rg", sb.Resource.ExistingResourceGroup);

        var other = builder.AddPulumiServiceBus("sb2").PublishAsExisting("ns2");
        Assert.Equal("ns2", other.Resource.ExistingNamespaceName);
        Assert.Null(other.Resource.ExistingResourceGroup);
    }

    [Fact]
    public void Registry_AsExisting_and_PublishAsExisting_record_registry_and_group()
    {
        var builder = DistributedApplication.CreateBuilder();
        var acr = builder.AddPulumiAzureContainerRegistry("acr").AsExisting("sharedacr", "shared-rg");
        Assert.Equal("sharedacr", acr.Resource.ExistingRegistryName);
        Assert.Equal("shared-rg", acr.Resource.ExistingResourceGroup);

        var other = builder.AddPulumiAzureContainerRegistry("acr2").PublishAsExisting("acr2name");
        Assert.Equal("acr2name", other.Resource.ExistingRegistryName);
        Assert.Null(other.Resource.ExistingResourceGroup);
    }

    [Fact]
    public void ContainerAppEnvironment_AsExisting_keeps_the_environment_group_separate_from_the_apps_group()
    {
        var builder = DistributedApplication.CreateBuilder();
        var env = builder.AddPulumiAzureContainerAppEnvironment("env").AsExisting("shared-env", "shared-rg");
        Assert.Equal("shared-env", env.Resource.ExistingEnvironmentName);
        Assert.Equal("shared-rg", env.Resource.ExistingEnvironmentResourceGroup);
        Assert.Null(env.Resource.ExistingResourceGroup);

        var other = builder.AddPulumiAzureContainerAppEnvironment("env2").PublishAsExisting("e2");
        Assert.Equal("e2", other.Resource.ExistingEnvironmentName);
        Assert.Null(other.Resource.ExistingEnvironmentResourceGroup);
    }

    [Fact]
    public void AsExisting_rejects_empty_names_and_groups()
    {
        var builder = DistributedApplication.CreateBuilder();
        Assert.Throws<ArgumentException>(() => builder.AddPulumiServiceBus("sb").AsExisting(""));
        Assert.Throws<ArgumentException>(() => builder.AddPulumiServiceBus("sb3").AsExisting("ns", ""));
        Assert.Throws<ArgumentException>(() => builder.AddPulumiAzureContainerRegistry("acr").AsExisting(""));
        Assert.Throws<ArgumentException>(() => builder.AddPulumiAzureContainerAppEnvironment("env").AsExisting("", null));
    }
}
