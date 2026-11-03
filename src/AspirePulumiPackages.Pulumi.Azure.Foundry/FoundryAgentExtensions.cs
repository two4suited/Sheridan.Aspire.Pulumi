using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Azure.Search;

namespace AspirePulumiPackages.Pulumi.Azure.Foundry;

/// <summary>
/// Pulumi equivalents of the prompt agent and tool extension methods of Aspire.Hosting.Foundry (<c>PromptAgentBuilderExtensions</c>).
/// Agents and tools are Foundry data-plane objects: they are created by pipeline steps after the Pulumi stack has deployed.
/// </summary>
public static class FoundryAgentExtensions
{
    /// <summary>Adds a prompt agent (mirrors <c>AddPromptAgent</c>); its name is the agent name in Foundry.</summary>
    public static IResourceBuilder<FoundryPromptAgentResource> AddPromptAgent(
        this IResourceBuilder<FoundryProjectResource> project, string name, IResourceBuilder<FoundryDeploymentResource> model, string? instructions = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var agent = new FoundryPromptAgentResource(name, model.Resource.DeploymentName, project.Resource, instructions);
        return project.ApplicationBuilder.AddResource(agent).WithReferenceRelationship(project).WithReferenceRelationship(model);
    }

    /// <summary>Attaches a tool to the agent (mirrors <c>WithTool</c>).</summary>
    public static IResourceBuilder<FoundryPromptAgentResource> WithTool(
        this IResourceBuilder<FoundryPromptAgentResource> agent, IResourceBuilder<FoundryToolResource> tool)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(tool);
        agent.Resource.AddTool(tool.Resource);
        return agent.WithReferenceRelationship(tool);
    }

    /// <summary>Attaches a custom tool definition to the agent (mirrors <c>WithCustomTool</c>).</summary>
    public static IResourceBuilder<FoundryPromptAgentResource> WithCustomTool(
        this IResourceBuilder<FoundryPromptAgentResource> agent, IFoundryTool tool)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(tool);
        agent.Resource.AddCustomTool(tool);
        return agent;
    }

    private static IResourceBuilder<T> AddTool<T>(IResourceBuilder<FoundryProjectResource> project, string name, T tool) where T : FoundryToolResource
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return project.ApplicationBuilder.AddResource(tool).WithReferenceRelationship(project);
    }

    public static IResourceBuilder<CodeInterpreterToolResource> AddCodeInterpreterTool(this IResourceBuilder<FoundryProjectResource> project, string name) =>
        AddTool(project, name, new CodeInterpreterToolResource(name, project.Resource));

    public static IResourceBuilder<FileSearchToolResource> AddFileSearchTool(this IResourceBuilder<FoundryProjectResource> project, string name, params string[] vectorStoreIds) =>
        AddTool(project, name, new FileSearchToolResource(name, project.Resource) { VectorStoreIds = vectorStoreIds.ToList() });

    public static IResourceBuilder<WebSearchToolResource> AddWebSearchTool(this IResourceBuilder<FoundryProjectResource> project, string name) =>
        AddTool(project, name, new WebSearchToolResource(name, project.Resource));

    public static IResourceBuilder<ImageGenerationToolResource> AddImageGenerationTool(this IResourceBuilder<FoundryProjectResource> project, string name) =>
        AddTool(project, name, new ImageGenerationToolResource(name, project.Resource));

    public static IResourceBuilder<ComputerToolResource> AddComputerUseTool(
        this IResourceBuilder<FoundryProjectResource> project, string name, int displayWidth = 1024, int displayHeight = 768, string environment = "browser") =>
        AddTool(project, name, new ComputerToolResource(name, project.Resource, displayWidth, displayHeight, environment));

    public static IResourceBuilder<AzureAISearchToolResource> AddAISearchTool(
        this IResourceBuilder<FoundryProjectResource> project, string name, string? indexName = null) =>
        AddTool(project, name, new AzureAISearchToolResource(name, project.Resource) { IndexName = indexName });

    /// <summary>Links an AI Search tool to a search resource (same stack): adds the project connection (and its role assignments) as <c>AddConnection(search)</c> does.</summary>
    public static IResourceBuilder<AzureAISearchToolResource> WithReference(
        this IResourceBuilder<AzureAISearchToolResource> tool, IResourceBuilder<AzureSearchResource> search)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(search);
        if (tool.Resource.Connection is not null)
            throw new InvalidOperationException($"Azure AI Search tool '{tool.Resource.Name}' already has a backing resource configured.");
        var connection = tool.ApplicationBuilder.CreateResourceBuilder(tool.Resource.Project).AddConnection(search);
        tool.Resource.Connection = connection.Resource;
        tool.Resource.SearchResource = search.Resource;
        return tool;
    }

    public static IResourceBuilder<BingGroundingToolResource> AddBingGroundingTool(this IResourceBuilder<FoundryProjectResource> project, string name) =>
        AddTool(project, name, new BingGroundingToolResource(name, project.Resource));

    /// <summary>Links a Bing grounding tool to a connection created with <c>AddBingGroundingConnection</c>.</summary>
    public static IResourceBuilder<BingGroundingToolResource> WithReference(
        this IResourceBuilder<BingGroundingToolResource> tool, IResourceBuilder<FoundryProjectConnectionResource> bingConnection)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(bingConnection);
        if (tool.Resource.Connection is not null)
            throw new InvalidOperationException($"Bing Grounding tool '{tool.Resource.Name}' already has a connection configured.");
        tool.Resource.Connection = bingConnection.Resource;
        return tool;
    }

    /// <summary>Links a Bing grounding tool to a new Bing connection; azure-native cannot list Bing keys, so the API key is a secret parameter.</summary>
    public static IResourceBuilder<BingGroundingToolResource> WithReference(
        this IResourceBuilder<BingGroundingToolResource> tool, string bingResourceId, IResourceBuilder<ParameterResource> apiKey)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (tool.Resource.Connection is not null)
            throw new InvalidOperationException($"Bing Grounding tool '{tool.Resource.Name}' already has a connection configured.");
        var connection = tool.ApplicationBuilder.CreateResourceBuilder(tool.Resource.Project)
            .AddBingGroundingConnection($"{tool.Resource.Name}-conn", bingResourceId, apiKey);
        tool.Resource.Connection = connection.Resource;
        return tool;
    }

    public static IResourceBuilder<SharePointToolResource> AddSharePointTool(
        this IResourceBuilder<FoundryProjectResource> project, string name, params string[] projectConnectionIds) =>
        AddTool(project, name, new SharePointToolResource(name, project.Resource, projectConnectionIds));

    public static IResourceBuilder<FabricToolResource> AddFabricTool(
        this IResourceBuilder<FoundryProjectResource> project, string name, params string[] projectConnectionIds) =>
        AddTool(project, name, new FabricToolResource(name, project.Resource, projectConnectionIds));

    public static IResourceBuilder<AzureFunctionToolResource> AddAzureFunctionTool(
        this IResourceBuilder<FoundryProjectResource> project, string name, string functionName, string description, BinaryData parameters,
        string inputQueueEndpoint, string inputQueueName, string outputQueueEndpoint, string outputQueueName) =>
        AddTool(project, name, new AzureFunctionToolResource(name, project.Resource, functionName, description, parameters,
            inputQueueEndpoint, inputQueueName, outputQueueEndpoint, outputQueueName));

    public static IResourceBuilder<AzureFunctionToolResource> AddAzureFunctionTool(
        this IResourceBuilder<FoundryProjectResource> project, string name, string functionName, string description, string parametersJson,
        string inputQueueEndpoint, string inputQueueName, string outputQueueEndpoint, string outputQueueName) =>
        project.AddAzureFunctionTool(name, functionName, description, BinaryData.FromString(parametersJson),
            inputQueueEndpoint, inputQueueName, outputQueueEndpoint, outputQueueName);

    public static IResourceBuilder<FunctionToolResource> AddFunctionTool(
        this IResourceBuilder<FoundryProjectResource> project, string name, string functionName, BinaryData parameters, string? description = null, bool? strictModeEnabled = null) =>
        AddTool(project, name, new FunctionToolResource(name, project.Resource, functionName, parameters, description, strictModeEnabled));

    public static IResourceBuilder<FunctionToolResource> AddFunctionTool(
        this IResourceBuilder<FoundryProjectResource> project, string name, string functionName, string parametersJson, string? description = null, bool? strictModeEnabled = null) =>
        project.AddFunctionTool(name, functionName, BinaryData.FromString(parametersJson), description, strictModeEnabled);
}
