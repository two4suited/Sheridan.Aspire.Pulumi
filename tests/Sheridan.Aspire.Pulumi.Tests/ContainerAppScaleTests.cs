using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging.Abstractions;
using Sheridan.Aspire.Pulumi.Azure.AppContainers;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

/// <summary>Container App scale settings and rules (issue #246).</summary>
public sealed class ContainerAppScaleTests
{
    private static readonly string ApiPath = TestProjects.Create("Api.csproj");

    private static async Task<Dictionary<string, object?>> ScaleAsync(Action<IResourceBuilder<global::Aspire.Hosting.Dotnet.DotnetProjectResource>>? configure)
    {
        var builder = DistributedApplication.CreateBuilder(["--publisher", "manifest", "--output-path", Path.GetTempPath()]);
        var env = builder.AddPulumiAzureContainerAppEnvironment("aca-env");
        var api = builder.AddDotnetProject("api", ApiPath).WithHttpEndpoint();
        configure?.Invoke(api);
        var app = await env.Resource.BuildContainerAppAsync(
            api.Resource, null, new PulumiStackResource("stack", "dev"), "rg", builder.ExecutionContext, NullLogger.Instance, false, CancellationToken.None);
        var template = (Dictionary<string, object?>)((Dictionary<string, object?>)app["properties"]!)["template"]!;
        return (Dictionary<string, object?>)template["scale"]!;
    }

    [Fact]
    public async Task Default_is_a_single_replica()
    {
        var scale = await ScaleAsync(null);
        Assert.Equal(1, scale["minReplicas"]);
        Assert.Equal(1, scale["maxReplicas"]);
        Assert.False(scale.ContainsKey("rules"));
    }

    [Fact]
    public async Task Replicas_cooldown_and_rules_are_emitted()
    {
        var scale = await ScaleAsync(api => api
            .WithPulumiContainerAppScale(0, 10, cooldownPeriod: 30)
            .WithPulumiHttpScaleRule("http-concurrency", 10)
            .WithPulumiCpuScaleRule("cpu", 70)
            .WithPulumiCustomScaleRule("queue", "azure-queue", new Dictionary<string, string> { ["queueName"] = "q" }, useEnvironmentIdentity: true));

        Assert.Equal(0, scale["minReplicas"]);
        Assert.Equal(10, scale["maxReplicas"]);
        Assert.Equal(30, scale["cooldownPeriod"]);
        var rules = ((List<object?>)scale["rules"]!).Cast<Dictionary<string, object?>>().ToList();
        Assert.Equal(["http-concurrency", "cpu", "queue"], rules.Select(r => (string)r["name"]!));
        var http = (Dictionary<string, object?>)rules[0]["http"]!;
        Assert.Equal("10", ((Dictionary<string, object?>)http["metadata"]!)["concurrentRequests"]);
        var cpu = (Dictionary<string, object?>)rules[1]["custom"]!;
        Assert.Equal("cpu", cpu["type"]);
        Assert.Equal("70", ((Dictionary<string, object?>)cpu["metadata"]!)["value"]);
        Assert.Equal("Utilization", ((Dictionary<string, object?>)cpu["metadata"]!)["type"]);
        Assert.StartsWith("${", (string)((Dictionary<string, object?>)rules[2]["custom"]!)["identity"]!);
    }

    [Fact]
    public async Task Rules_raise_the_default_max_and_cooldown_is_kept()
    {
        var scale = await ScaleAsync(api => api.PublishAsPulumiContainerApp(s => s.CooldownPeriod = 60).WithPulumiContainerAppScale(0, 5).WithPulumiMemoryScaleRule("mem", 80));
        Assert.Equal(60, scale["cooldownPeriod"]);
        var s2 = await ScaleAsync(api => api.WithPulumiHttpScaleRule("h", 5));
        Assert.Equal(10, s2["maxReplicas"]);
    }

    [Fact]
    public async Task Invalid_replica_range_throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => ScaleAsync(api => api.WithPulumiContainerAppScale(5, 2)));
    }

    [Fact]
    public void Duplicate_rule_names_throw()
    {
        var s = new PulumiContainerAppScale().AddHttpRule("r", 1);
        Assert.Throws<ArgumentException>(() => s.AddHttpRule("r", 2));
    }
}
