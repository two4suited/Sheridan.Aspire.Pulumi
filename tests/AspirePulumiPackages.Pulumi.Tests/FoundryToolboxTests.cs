using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Azure.Foundry;
using Xunit;

namespace AspirePulumiPackages.Pulumi.Tests;

public sealed class FoundryToolboxTests
{
    private static FoundryToolboxResource NewToolbox() => new("tools", new FoundryProjectResource("proj", new FoundryResource("foundry")));

    [Fact]
    public void Tool_names_must_be_unique()
    {
        var toolbox = NewToolbox();
        toolbox.AddTool(new FoundryToolboxWebSearchToolDefinition("web", null));
        Assert.Throws<InvalidOperationException>(() => toolbox.AddTool(new FoundryToolboxWebSearchToolDefinition("web", "again")));
    }

    [Fact]
    public async Task Mcp_tool_resolves_to_an_mcp_definition_and_rejects_local_endpoints()
    {
        var ok = new FoundryToolboxMcpToolDefinition("docs", ReferenceExpression.Create($"https://example.com/mcp"), new FoundryToolboxMcpToolOptions { ServerLabel = "d" });
        Assert.NotNull(await ok.ResolveAsync(CancellationToken.None));

        var local = new FoundryToolboxMcpToolDefinition("docs", ReferenceExpression.Create($"https://localhost:5001/mcp"), null);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await local.ResolveAsync(CancellationToken.None));
    }
}
