using System.ClientModel;
using System.Globalization;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Aspire.Hosting.Publishing;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Sheridan.Aspire.Pulumi.Azure.Foundry;

/// <summary>
/// A Foundry prompt agent: a model deployment, instructions and tools, created in the project through the Foundry Agents data plane
/// (<c>AIProjectClient</c>) by a pipeline step that runs once the Pulumi stack with the project has deployed. Counterpart of
/// <c>AzurePromptAgentResource</c> in Aspire.Hosting.Foundry (minus its run-mode deployment, because the project only exists after
/// <c>aspire deploy</c> / <c>aspire do deploy</c>). Connection string: <c>&lt;project endpoint&gt;/agents/&lt;name&gt;</c>.
/// </summary>
public sealed class FoundryPromptAgentResource : Resource, IResourceWithConnectionString, IResourceWithParent<FoundryProjectResource>
{
    private const int ProjectEndpointReadinessMaxRetryAttempts = 11;
    private static readonly TimeSpan s_projectEndpointReadinessDelay = TimeSpan.FromSeconds(5);

    private readonly List<IFoundryTool> _tools = [];

    public FoundryPromptAgentResource(string name, string model, FoundryProjectResource project, string? instructions = null) : base(name)
    {
        ArgumentException.ThrowIfNullOrEmpty(model);
        Model = model;
        Project = project ?? throw new ArgumentNullException(nameof(project));
        Instructions = instructions;
        Metadata = new Dictionary<string, string>
        {
            ["DeployedBy"] = "Aspire Hosting Framework (Pulumi)",
            ["DeployedOn"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
        };

        Annotations.Add(new PipelineStepAnnotation(_ => Task.FromResult<IEnumerable<PipelineStep>>(
        [
            new PipelineStep
            {
                Name = $"deploy-{Name}",
                Description = $"Deploys prompt agent {Name}.",
                Action = async stepCtx =>
                {
                    var version = await DeployAsync(stepCtx.Logger, stepCtx.CancellationToken);
                    stepCtx.ReportingStep.Log(LogLevel.Information, new MarkdownString($"Successfully deployed **{Name}** as Prompt Agent (version {version.Version})"));
                    Version.Set(version.Version);
                },
                Tags = [WellKnownPipelineTags.DeployCompute],
                RequiredBySteps = [WellKnownPipelineSteps.Deploy],
                DependsOnSteps = [WellKnownPipelineSteps.DeployPrereq],
                Resource = this,
            },
        ])));

        // The agent can only be created once the stack that holds the project (and the connections/tools' targets) has been deployed.
        Annotations.Add(new PipelineConfigurationAnnotation(context =>
        {
            var stack = Project.Parent.Stack ?? context.Model.Resources.OfType<PulumiStackResource>().SingleOrDefault();
            if (stack is null) return Task.CompletedTask;
            var provision = context.GetSteps(stack, WellKnownPipelineTags.ProvisionInfrastructure);
            context.GetSteps(this, WellKnownPipelineTags.DeployCompute).DependsOn(provision);
            return Task.CompletedTask;
        }));
    }

    /// <summary>The model deployment name the agent uses.</summary>
    public string Model { get; set; }

    public FoundryProjectResource Project { get; }

    FoundryProjectResource IResourceWithParent<FoundryProjectResource>.Parent => Project;

    public string? Instructions { get; set; }

    public string Description { get; set; } = "Prompt Agent";

    public IDictionary<string, string> Metadata { get; }

    /// <summary>The version Foundry assigned to the agent once it has been deployed.</summary>
    public FoundryStaticValue Version { get; } = new();

    public IReadOnlyList<IFoundryTool> Tools => _tools;

    public ReferenceExpression ConnectionStringExpression => ReferenceExpression.Create($"{Project.Endpoint}/agents/{Name}");

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties()
    {
        yield return new("AgentName", ReferenceExpression.Create($"{Name}"));
        yield return new("ProjectEndpoint", ReferenceExpression.Create($"{Project.Endpoint}"));
        yield return new("ConnectionString", ConnectionStringExpression);
    }

    internal void AddTool(FoundryToolResource tool)
    {
        if (tool.Project != Project)
            throw new InvalidOperationException(
                $"Tool '{tool.Name}' belongs to project '{tool.Project.Name}' but agent '{Name}' belongs to project '{Project.Name}'. All tools must belong to the same project as the agent.");
        _tools.Add(tool);
    }

    internal void AddCustomTool(IFoundryTool tool) => _tools.Add(tool);

    /// <summary>Creates a new version of the agent in the project.</summary>
    internal async Task<ProjectsAgentVersion> DeployAsync(ILogger logger, CancellationToken cancellationToken)
    {
        var projectEndpoint = await Project.Endpoint.GetValueAsync(cancellationToken);
        if (string.IsNullOrEmpty(projectEndpoint))
            throw new InvalidOperationException($"Project '{Project.Name}' does not have a valid endpoint.");

        var options = await ToCreationOptionsAsync(cancellationToken);
        var client = new AIProjectClient(new Uri(projectEndpoint), new DefaultAzureCredential());
        var pipeline = new ResiliencePipelineBuilder<ProjectsAgentVersion>().AddRetry(new RetryStrategyOptions<ProjectsAgentVersion>
        {
            Delay = s_projectEndpointReadinessDelay,
            MaxRetryAttempts = ProjectEndpointReadinessMaxRetryAttempts,
            ShouldHandle = new PredicateBuilder<ProjectsAgentVersion>().Handle<ClientResultException>(IsProjectEndpointNotReady),
            OnRetry = retry =>
            {
                logger.LogWarning("Foundry project endpoint for '{Project}' is not ready yet. Retrying prompt agent deployment in {Seconds:n0} seconds ({Attempt}/{Max}).",
                    Project.Name, s_projectEndpointReadinessDelay.TotalSeconds, retry.AttemptNumber + 1, ProjectEndpointReadinessMaxRetryAttempts);
                return ValueTask.CompletedTask;
            },
        }).Build();

        return await pipeline.ExecuteAsync(async ct =>
            (await client.AgentAdministrationClient.CreateAgentVersionAsync(Name, options, null, ct)).Value, cancellationToken);
    }

    /// <summary>Builds the agent definition, resolving every tool (and so any stack output it needs) at deploy time.</summary>
    internal async Task<ProjectsAgentVersionCreationOptions> ToCreationOptionsAsync(CancellationToken cancellationToken)
    {
        var definition = new DeclarativeAgentDefinition(Model) { Instructions = Instructions };
        foreach (var tool in _tools) definition.Tools.Add(await tool.ToAgentToolAsync(cancellationToken));

        var options = new ProjectsAgentVersionCreationOptions(definition) { Description = Description };
        foreach (var (key, value) in Metadata) options.Metadata[key] = value;
        return options;
    }

    private static bool IsProjectEndpointNotReady(ClientResultException ex) =>
        ex.Status == 404 &&
        (ex.Message.Contains("Subdomain does not map to a resource", StringComparison.OrdinalIgnoreCase)
         || ex.Message.Contains("The project does not exist", StringComparison.OrdinalIgnoreCase));
}

/// <summary>A value that is set once, after the pipeline step that produces it has run (for example an agent version).</summary>
public sealed class FoundryStaticValue : IValueProvider, IManifestExpressionProvider
{
    private string? _value;
    private bool _isSet;

    public string ValueExpression => "{value}";

    public void Set(string? value)
    {
        if (_isSet) throw new InvalidOperationException("Value has already been set.");
        _value = value;
        _isSet = true;
    }

    public ValueTask<string?> GetValueAsync(CancellationToken cancellationToken = default) =>
        _isSet ? ValueTask.FromResult(_value) : throw new InvalidOperationException("Value for provider has not been set.");

    public ValueTask<string?> GetValueAsync(ValueProviderContext context, CancellationToken cancellationToken = default) =>
        GetValueAsync(cancellationToken);
}
