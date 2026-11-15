using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure.AppContainers;
using Sheridan.Aspire.Pulumi.Azure.Base;
using Sheridan.Aspire.Pulumi.Azure.Network;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

/// <summary>The Container Apps environment as a private endpoint target (issue #214).</summary>
public sealed class ContainerAppPrivateEndpointTests
{
    [Fact]
    public void Environment_is_a_private_endpoint_target_for_managed_environments()
    {
        var builder = DistributedApplication.CreateBuilder();
        var env = builder.AddPulumiAzureContainerAppEnvironment("aca").WithLocation("westeurope");
        var target = (IPulumiPrivateEndpointTarget)env.Resource;

        Assert.Equal("managedEnvironments", target.PrivateLinkGroupId);
        Assert.Equal("privatelink.westeurope.azurecontainerapps.io", target.PrivateDnsZoneName);
        Assert.Equal($"${{{env.Resource.Id}.id}}", target.IdExpression);
    }

    [Fact]
    public void Private_endpoint_on_the_environment_is_detected()
    {
        var builder = DistributedApplication.CreateBuilder();
        var vnet = builder.AddPulumiVirtualNetwork("vnet", "10.0.0.0/16");
        var peSubnet = vnet.AddSubnet("pe", "10.0.2.0/24");
        var env = builder.AddPulumiAzureContainerAppEnvironment("aca");

        var model = new DistributedApplicationModel(builder.Resources);
        Assert.False(env.Resource.HasPrivateEndpoint(model));

        peSubnet.AddPrivateEndpoint(env);
        Assert.True(env.Resource.HasPrivateEndpoint(model));
    }
}
