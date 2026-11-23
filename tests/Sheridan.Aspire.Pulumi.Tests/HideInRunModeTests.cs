using Sheridan.Aspire.Pulumi.Azure.Base;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure.Kubernetes;
using Sheridan.Aspire.Pulumi.Azure.Network;
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
    public void Network_and_kubernetes_resources_are_hidden_in_run_mode()
    {
        var builder = DistributedApplication.CreateBuilder();
        var vnet = builder.AddPulumiVirtualNetwork("vnet");
        var subnet = vnet.AddSubnet("snet", "10.0.0.0/24").AllowInbound("443");
        var nsg = builder.AddPulumiNetworkSecurityGroup("nsg");
        var ip = builder.AddPulumiPublicIPAddress("ip");
        var nat = builder.AddPulumiNatGateway("nat");
        var aks = builder.AddPulumiKubernetes("aks");
        var pool = aks.AddNodePool("user1");
        var cert = aks.AddCertManager("cm");
        var issuer = cert.AddIssuer("issuer");
        var ingress = aks.AddIngress("ing");
        var gateway = aks.AddGateway("gw");
        var chart = aks.AddHelmChart("chart", "oci://example/chart", "1.0.0");

        var all = new IResource[] { vnet.Resource, subnet.Resource, subnet.Resource.NetworkSecurityGroup!, nsg.Resource, ip.Resource, nat.Resource,
            aks.Resource, pool.Resource, cert.Resource, issuer.Resource, ingress.Resource, gateway.Resource, chart.Resource };
        Assert.All(all, r => Assert.True(IsHidden(r), r.Name));
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
