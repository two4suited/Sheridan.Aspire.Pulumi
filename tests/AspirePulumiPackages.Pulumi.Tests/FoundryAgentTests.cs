using AspirePulumiPackages.Pulumi.Azure.Foundry;
using Azure.AI.Projects.Agents;
using Xunit;

namespace AspirePulumiPackages.Pulumi.Tests;

/// <summary>The data-plane definition of Foundry prompt agents (built when the deploy step runs, from the model and the attached tools).</summary>
public sealed class FoundryAgentTests
{
    private static FoundryProjectResource NewProject(string name = "proj") => new(name, new FoundryResource("foundry"));

    [Fact]
    public async Task Creation_options_carry_model_instructions_description_metadata_and_tools()
    {
        var project = NewProject();
        var agent = new FoundryPromptAgentResource("helper", "gpt-4o", project, "Be brief.") { Description = "Helps" };
        agent.AddTool(new CodeInterpreterToolResource("ci", project));
        agent.AddTool(new WebSearchToolResource("web", project));
        agent.AddTool(new FunctionToolResource("fn", project, "get_weather", BinaryData.FromString("""{"type":"object","properties":{}}"""), "Weather"));

        var options = await agent.ToCreationOptionsAsync(CancellationToken.None);

        var definition = Assert.IsType<DeclarativeAgentDefinition>(options.Definition);
        Assert.Equal("gpt-4o", definition.Model);
        Assert.Equal("Be brief.", definition.Instructions);
        Assert.Equal(3, definition.Tools.Count);
        Assert.Equal("Helps", options.Description);
        Assert.Equal("Aspire Hosting Framework (Pulumi)", options.Metadata["DeployedBy"]);
    }

    [Fact]
    public void Tools_must_belong_to_the_agents_project()
    {
        var agent = new FoundryPromptAgentResource("helper", "gpt-4o", NewProject("a"));
        var ex = Assert.Throws<InvalidOperationException>(() => agent.AddTool(new WebSearchToolResource("web", NewProject("b"))));
        Assert.Contains("same project", ex.Message);
    }

    [Fact]
    public async Task Search_and_bing_tools_require_a_connection()
    {
        var project = NewProject();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AzureAISearchToolResource("s", project).ToAgentToolAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new BingGroundingToolResource("b", project).ToAgentToolAsync());
    }

    [Fact]
    public async Task Version_can_only_be_set_once_and_not_read_before()
    {
        var agent = new FoundryPromptAgentResource("helper", "gpt-4o", NewProject());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await agent.Version.GetValueAsync());
        agent.Version.Set("3");
        Assert.Equal("3", await agent.Version.GetValueAsync());
        Assert.Throws<InvalidOperationException>(() => agent.Version.Set("4"));
    }
}
