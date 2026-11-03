using Aspire.Hosting.ApplicationModel;
using Azure.AI.Projects.Agents;
using AspirePulumiPackages.Pulumi.Azure.Search;
using OpenAI.Responses;

namespace AspirePulumiPackages.Pulumi.Azure.Foundry;

/// <summary>A tool that can be attached to a Foundry prompt agent (mirrors the official <c>IFoundryTool</c>).</summary>
public interface IFoundryTool
{
    /// <summary>Builds the agent tool definition; called when the agent is deployed, so stack outputs are available.</summary>
    Task<ResponseTool> ToAgentToolAsync(CancellationToken cancellationToken = default);
}

/// <summary>Base class of the tool resources a <see cref="FoundryProjectResource"/> offers to its prompt agents.</summary>
public abstract class FoundryToolResource(string name, FoundryProjectResource project)
    : Resource(name), IFoundryTool, IResourceWithParent<FoundryProjectResource>
{
    internal static ResponseTool Wrap(ProjectsAgentTool tool) => tool;

    public FoundryProjectResource Project { get; } = project ?? throw new ArgumentNullException(nameof(project));

    FoundryProjectResource IResourceWithParent<FoundryProjectResource>.Parent => Project;

    public abstract Task<ResponseTool> ToAgentToolAsync(CancellationToken cancellationToken = default);
}

public sealed class CodeInterpreterToolResource(string name, FoundryProjectResource project) : FoundryToolResource(name, project)
{
    public override Task<ResponseTool> ToAgentToolAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<ResponseTool>(new CodeInterpreterTool(new CodeInterpreterToolContainer(new AutomaticCodeInterpreterToolContainerConfiguration())));
}

public sealed class FileSearchToolResource(string name, FoundryProjectResource project) : FoundryToolResource(name, project)
{
    public IList<string> VectorStoreIds { get; init; } = [];

    public override Task<ResponseTool> ToAgentToolAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<ResponseTool>(ResponseTool.CreateFileSearchTool(VectorStoreIds, null, null, null));
}

public sealed class WebSearchToolResource(string name, FoundryProjectResource project) : FoundryToolResource(name, project)
{
    public override Task<ResponseTool> ToAgentToolAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<ResponseTool>(ResponseTool.CreateWebSearchTool(null, null, null));
}

public sealed class ImageGenerationToolResource(string name, FoundryProjectResource project) : FoundryToolResource(name, project)
{
    public override Task<ResponseTool> ToAgentToolAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<ResponseTool>(new ImageGenerationTool());
}

public sealed class ComputerToolResource(string name, FoundryProjectResource project, int displayWidth = 1024, int displayHeight = 768, string environment = "browser")
    : FoundryToolResource(name, project)
{
    public int DisplayWidth { get; } = displayWidth;
    public int DisplayHeight { get; } = displayHeight;
    public string Environment { get; } = environment;

    public override Task<ResponseTool> ToAgentToolAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<ResponseTool>(new ComputerTool(new ComputerToolEnvironment(Environment), DisplayWidth, DisplayHeight));
}

/// <summary>An Azure AI Search tool; the search resource is linked with <c>WithReference(search)</c>, which adds the project connection.</summary>
public sealed class AzureAISearchToolResource(string name, FoundryProjectResource project) : FoundryToolResource(name, project)
{
    public AzureSearchResource? SearchResource { get; internal set; }
    public string? IndexName { get; set; }
    internal FoundryProjectConnectionResource? Connection { get; set; }

    public override async Task<ResponseTool> ToAgentToolAsync(CancellationToken cancellationToken = default)
    {
        var connection = Connection ?? throw new InvalidOperationException(
            $"Azure AI Search tool '{Name}' does not have a backing resource configured. Call .WithReference(searchResource) to link it to an Azure AI Search resource.");
        var id = await connection.IdOutput.GetValueAsync(cancellationToken);
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException($"Failed to resolve connection ID for Azure AI Search tool '{Name}'. The Foundry project connection may not have been provisioned correctly.");
        var options = new AzureAISearchToolOptions([new AzureAISearchToolIndex { ProjectConnectionId = id, IndexName = IndexName }]);
        return Wrap(new AzureAISearchTool(options));
    }
}

/// <summary>A Bing grounding tool; the connection is linked with <c>WithReference(connection)</c>.</summary>
public sealed class BingGroundingToolResource(string name, FoundryProjectResource project) : FoundryToolResource(name, project)
{
    internal FoundryProjectConnectionResource? Connection { get; set; }

    public override async Task<ResponseTool> ToAgentToolAsync(CancellationToken cancellationToken = default)
    {
        var connection = Connection ?? throw new InvalidOperationException(
            $"Bing Grounding tool '{Name}' does not have a project connection. Call .WithReference(connection) with AddBingGroundingConnection().");
        var id = await connection.IdOutput.GetValueAsync(cancellationToken);
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException($"Failed to resolve connection ID for Bing Grounding tool '{Name}'. The Foundry project connection may not have been provisioned correctly.");
        var options = new BingGroundingSearchToolOptions([new BingGroundingSearchConfiguration(id)]);
        return Wrap(new BingGroundingTool(options));
    }
}

public sealed class SharePointToolResource(string name, FoundryProjectResource project, params string[] projectConnectionIds) : FoundryToolResource(name, project)
{
    public IList<string> ProjectConnectionIds { get; } = projectConnectionIds.ToList();

    public override Task<ResponseTool> ToAgentToolAsync(CancellationToken cancellationToken = default)
    {
        var options = new SharePointGroundingToolOptions();
        foreach (var id in ProjectConnectionIds) options.ProjectConnections.Add(new ToolProjectConnection(id));
        return Task.FromResult(Wrap(new SharepointPreviewTool(options)));
    }
}

public sealed class FabricToolResource(string name, FoundryProjectResource project, params string[] projectConnectionIds) : FoundryToolResource(name, project)
{
    public IList<string> ProjectConnectionIds { get; } = projectConnectionIds.ToList();

    public override Task<ResponseTool> ToAgentToolAsync(CancellationToken cancellationToken = default)
    {
        var options = new FabricDataAgentToolOptions();
        foreach (var id in ProjectConnectionIds) options.ProjectConnections.Add(new ToolProjectConnection(id));
        return Task.FromResult(Wrap(new MicrosoftFabricPreviewTool(options)));
    }
}

public sealed class FunctionToolResource(string name, FoundryProjectResource project, string functionName, BinaryData parameters, string? description = null, bool? strictModeEnabled = null)
    : FoundryToolResource(name, project)
{
    public string FunctionName { get; } = functionName;
    public BinaryData Parameters { get; } = parameters;
    public string? Description { get; } = description;
    public bool? StrictModeEnabled { get; } = strictModeEnabled;

    public override Task<ResponseTool> ToAgentToolAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<ResponseTool>(ResponseTool.CreateFunctionTool(FunctionName, Parameters, StrictModeEnabled, Description));
}

public sealed class AzureFunctionToolResource(string name, FoundryProjectResource project, string functionName, string description, BinaryData parameters,
    string inputQueueEndpoint, string inputQueueName, string outputQueueEndpoint, string outputQueueName) : FoundryToolResource(name, project)
{
    public string FunctionName { get; } = functionName;
    public string Description { get; } = description;
    public BinaryData Parameters { get; } = parameters;
    public string InputQueueEndpoint { get; } = inputQueueEndpoint;
    public string InputQueueName { get; } = inputQueueName;
    public string OutputQueueEndpoint { get; } = outputQueueEndpoint;
    public string OutputQueueName { get; } = outputQueueName;

    public override Task<ResponseTool> ToAgentToolAsync(CancellationToken cancellationToken = default)
    {
        var function = new AzureFunctionDefinitionFunction(FunctionName, Parameters) { Description = Description };
        var definition = new AzureFunctionDefinition(function,
            new AzureFunctionBinding(new AzureFunctionStorageQueue(InputQueueEndpoint, InputQueueName)),
            new AzureFunctionBinding(new AzureFunctionStorageQueue(OutputQueueEndpoint, OutputQueueName)));
        return Task.FromResult(Wrap(new AzureFunctionTool(definition)));
    }
}
