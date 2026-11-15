using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Logging.Abstractions;
using Sheridan.Aspire.Pulumi.Azure.AppContainers;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

/// <summary>WithoutAcrPullRoleAssignment (issue #212): no pull identity, registry auth or role assignment, for emulators without Microsoft.Authorization.</summary>
public sealed class AcrPullOptOutTests
{
    private sealed class ApiProject : IProjectMetadata
    {
        public string ProjectPath => Path.Combine(Path.GetTempPath(), "sheridan-tests-no-such-dir", "Api.csproj");

        public LaunchSettings? LaunchSettings => new();
    }

    private static async Task<(Dictionary<string, object?> App, Dictionary<string, string?> Env)> BuildAsync(bool optOut, bool job)
    {
        var builder = DistributedApplication.CreateBuilder(["--publisher", "manifest", "--output-path", Path.GetTempPath()]);
        var env = builder.AddPulumiAzureContainerAppEnvironment("aca-env");
        if (optOut) env.WithoutAcrPullRoleAssignment();
        var api = builder.AddProject<ApiProject>("api").WithHttpEndpoint();
        var stack = new PulumiStackResource("stack", "dev");
        var app = await env.Resource.BuildContainerAppAsync(
            api.Resource, job ? new PulumiContainerAppJobAnnotation(null) : null, stack, "rg", builder.ExecutionContext, NullLogger.Instance, false, CancellationToken.None);
        var entries = await env.Resource.BuildEnvironmentAsync(api.Resource, builder.ExecutionContext, NullLogger.Instance, stack: null, CancellationToken.None);
        return (app, entries.Cast<Dictionary<string, object?>>().ToDictionary(e => (string)e["name"]!, e => (string?)e["value"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Default_attaches_the_pull_identity_and_registry_auth(bool job)
    {
        var (app, env) = await BuildAsync(optOut: false, job);
        var props = (Dictionary<string, object?>)app["properties"]!;
        var config = (Dictionary<string, object?>)props["configuration"]!;

        Assert.True(props.ContainsKey("identity"));
        Assert.True(config.ContainsKey("registries"));
        Assert.True(app.ContainsKey("options"));
        Assert.True(env.ContainsKey("AZURE_CLIENT_ID"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Opt_out_omits_identity_registries_dependsOn_and_client_id(bool job)
    {
        var (app, env) = await BuildAsync(optOut: true, job);
        var props = (Dictionary<string, object?>)app["properties"]!;
        var config = (Dictionary<string, object?>)props["configuration"]!;

        Assert.False(props.ContainsKey("identity"));
        Assert.False(config.ContainsKey("registries"));
        Assert.False(app.ContainsKey("options"));
        Assert.False(env.ContainsKey("AZURE_CLIENT_ID"));
    }
}
