using Aspire.Hosting;
using Sheridan.Aspire.Pulumi;
using Sheridan.Aspire.Pulumi.Azure.Base;
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
        Assert.Equal("shared-rg", sb.Resource.ExistingNamespaceResourceGroup);
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
        Assert.Equal("shared-rg", acr.Resource.ExistingRegistryResourceGroup);
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

    private static Dictionary<string, object?> Invoke(PulumiProgram p, string id) =>
        (Dictionary<string, object?>)((Dictionary<string, object?>)p.Variables[id]!)["fn::invoke"]!;

    private static object? Arg(Dictionary<string, object?> invoke, string key) =>
        ((Dictionary<string, object?>)invoke["arguments"]!)[key];

    [Fact]
    public void ServiceBus_lookup_uses_the_namespaces_group_even_with_a_different_configured_group()
    {
        var builder = DistributedApplication.CreateBuilder();
        var sb = builder.AddPulumiServiceBus("messaging").AsExisting("shared-ns", "shared-rg");
        var program = new PulumiProgram();
        var rg = sb.Resource.AddExistingLookup(program, "${otherRg.name}");

        Assert.Equal("shared-rg", rg);
        var invoke = Invoke(program, sb.Resource.Id);
        Assert.Equal("azure-native:servicebus:getNamespace", invoke["function"]);
        Assert.Equal("shared-ns", Arg(invoke, "namespaceName"));
        Assert.Equal("shared-rg", Arg(invoke, "resourceGroupName"));
        Assert.DoesNotContain(sb.Resource.Id, program.Resources.Keys);
    }

    [Fact]
    public void ServiceBus_without_any_resource_group_throws_at_generation()
    {
        var builder = DistributedApplication.CreateBuilder();
        var sb = builder.AddPulumiServiceBus("messaging").AsExisting("shared-ns");
        Assert.Throws<InvalidOperationException>(() => sb.Resource.AddExistingLookup(new PulumiProgram(), "${x.name}"));

        var withGroup = builder.AddPulumiServiceBus("m2").AsExisting("shared-ns").WithExistingResourceGroup("rg-x");
        Assert.Equal("rg-x", withGroup.Resource.AddExistingLookup(new PulumiProgram(), "rg-x"));
    }

    [Fact]
    public void Registry_lookup_is_emitted_and_guards_apply()
    {
        var builder = DistributedApplication.CreateBuilder();
        var acr = builder.AddPulumiAzureContainerRegistry("acr").AsExisting("sharedacr", "shared-rg");
        var program = new PulumiProgram();
        acr.Resource.AddExistingLookup(program, "${other.name}");
        var invoke = Invoke(program, acr.Resource.Id);
        Assert.Equal("azure-native:containerregistry:getRegistry", invoke["function"]);
        Assert.Equal("sharedacr", Arg(invoke, "registryName"));
        Assert.Equal("shared-rg", Arg(invoke, "resourceGroupName"));

        acr.WithPurgeTask("0 1 * * *");
        Assert.Throws<InvalidOperationException>(() => acr.Resource.AddExistingLookup(new PulumiProgram(), "x"));

        var noGroup = builder.AddPulumiAzureContainerRegistry("acr2").AsExisting("r");
        Assert.Throws<InvalidOperationException>(() => noGroup.Resource.AddExistingLookup(new PulumiProgram(), "x"));
    }

    [Fact]
    public void ContainerAppEnvironment_lookup_and_guards()
    {
        var builder = DistributedApplication.CreateBuilder();
        var acr = builder.AddPulumiAzureContainerRegistry("acr").AsExisting("sharedacr", "shared-rg");
        var env = builder.AddPulumiAzureContainerAppEnvironment("aca-env").AsExisting("shared-env", "shared-rg");

        // No registry yet.
        Assert.Throws<InvalidOperationException>(() => env.Resource.ValidateExisting("x"));

        env.WithAzureContainerRegistry(acr);
        env.Resource.ValidateExisting("x");
        var program = new PulumiProgram();
        env.Resource.AddExistingLookup(program, "x");
        var invoke = Invoke(program, env.Resource.Id);
        Assert.Equal("azure-native:app:getManagedEnvironment", invoke["function"]);
        Assert.Equal("shared-env", Arg(invoke, "environmentName"));
        Assert.Equal("shared-rg", Arg(invoke, "resourceGroupName"));
        Assert.DoesNotContain(env.Resource.Id, program.Resources.Keys);

        // Explicit dashboard opt-in is rejected; opting out is fine.
        env.WithDashboard(true);
        Assert.Throws<InvalidOperationException>(() => env.Resource.ValidateExisting("x"));
        env.WithDashboard(false);
        env.Resource.ValidateExisting("x");
    }

    [Fact]
    public void ContainerAppEnvironment_without_any_resource_group_throws()
    {
        var builder = DistributedApplication.CreateBuilder();
        var acr = builder.AddPulumiAzureContainerRegistry("acr").AsExisting("sharedacr", "shared-rg");
        var env = builder.AddPulumiAzureContainerAppEnvironment("aca-env").AsExisting("shared-env").WithAzureContainerRegistry(acr);
        Assert.Throws<InvalidOperationException>(() => env.Resource.ValidateExisting("x"));
        env.WithExistingResourceGroup("apps-rg");
        env.Resource.ValidateExisting("apps-rg");
    }
}
