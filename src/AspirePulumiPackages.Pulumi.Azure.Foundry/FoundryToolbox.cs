using System.ClientModel;
using System.ClientModel.Primitives;
using System.IO.Hashing;
using System.Text;
using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Aspire.Hosting.Publishing;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace AspirePulumiPackages.Pulumi.Azure.Foundry;

/// <summary>Options of a toolbox MCP tool (mirrors <c>FoundryToolboxMcpToolOptions</c>, minus the approval policy).</summary>
public sealed class FoundryToolboxMcpToolOptions
{
    public string? ServerLabel { get; set; }
    public string? ServerDescription { get; set; }
}

/// <summary>A tool of a Foundry toolbox, resolved when the toolbox is deployed.</summary>
public abstract class FoundryToolboxToolDefinition(string name)
{
    public string Name { get; } = name;
    internal abstract ValueTask<ProjectsAgentTool> ResolveAsync(CancellationToken cancellationToken);
}

internal sealed class FoundryToolboxWebSearchToolDefinition(string name, string? description) : FoundryToolboxToolDefinition(name)
{
    internal override ValueTask<ProjectsAgentTool> ResolveAsync(CancellationToken cancellationToken)
    {
        var json = new Dictionary<string, string> { ["type"] = "web_search", ["name"] = Name };
        if (description is not null) json["description"] = description;
        return new(FoundryToolbox.ReadTool(JsonSerializer.Serialize(json)));
    }
}

internal sealed class FoundryToolboxMcpToolDefinition(string name, ReferenceExpression endpoint, FoundryToolboxMcpToolOptions? options) : FoundryToolboxToolDefinition(name)
{
    public ReferenceExpression EndpointExpression { get; } = endpoint;
    public string ServerLabel { get; } = options?.ServerLabel ?? name;

    internal static bool IsFoundryReachableHttpsEndpoint(Uri uri)
    {
        var host = uri.Host.TrimEnd('.');
        return uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) && !uri.IsLoopback
            && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase) && !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    internal override async ValueTask<ProjectsAgentTool> ResolveAsync(CancellationToken cancellationToken)
    {
        var text = await EndpointExpression.GetValueAsync(cancellationToken);
        if (string.IsNullOrEmpty(text)) throw new InvalidOperationException($"MCP tool '{Name}' does not have a resolvable endpoint URI.");
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !IsFoundryReachableHttpsEndpoint(uri))
            throw new InvalidOperationException($"MCP tool '{Name}' must resolve to a Foundry-reachable absolute HTTPS endpoint.");
        var json = new Dictionary<string, string> { ["type"] = "mcp", ["server_label"] = ServerLabel, ["server_url"] = uri.AbsoluteUri };
        if (options?.ServerDescription is { } description) json["server_description"] = description;
        return FoundryToolbox.ReadTool(JsonSerializer.Serialize(json));
    }
}

internal sealed class FoundryToolboxAISearchToolDefinition(string name, FoundryProjectConnectionResource connection, string indexName) : FoundryToolboxToolDefinition(name)
{
    public FoundryProjectConnectionResource Connection { get; } = connection;

    internal override async ValueTask<ProjectsAgentTool> ResolveAsync(CancellationToken cancellationToken)
    {
        var id = await Connection.IdOutput.GetValueAsync(cancellationToken);
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException($"Failed to resolve connection ID for Azure AI Search tool '{Name}'. The Foundry project connection may not have been provisioned correctly.");
        return new AzureAISearchTool(new AzureAISearchToolOptions([new AzureAISearchToolIndex { ProjectConnectionId = id, IndexName = indexName }]));
    }
}

/// <summary>
/// A Foundry toolbox (versioned set of tools served as one MCP endpoint), reconciled through the Agents data plane after the stack
/// has deployed. Counterpart of <c>FoundryToolboxResource</c>: a version is created only when the configuration hash differs from the
/// current default (which must be Aspire-managed), then promoted to default. Run-mode deployment, the tool-discovery readiness probe,
/// <c>AsExisting</c> and MCP approval policies are not ported.
/// </summary>
public sealed class FoundryToolboxResource : Resource, IResourceWithConnectionString, IResourceWithParent<FoundryProjectResource>
{
    internal const string ManagedByKey = "aspire-managed-by", ManagedByValue = "Aspire.Hosting.Foundry", HashKey = "aspire-configuration-hash", SchemaKey = "aspire-schema-version";
    private readonly List<FoundryToolboxToolDefinition> _tools = [];

    public FoundryToolboxResource(string name, FoundryProjectResource parent) : base(name)
    {
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));
        Annotations.Add(new PipelineStepAnnotation(_ => Task.FromResult<IEnumerable<PipelineStep>>(
        [
            new PipelineStep
            {
                Name = $"deploy-{Name}",
                Description = $"Reconciles Toolbox {Name}.",
                Action = async ctx =>
                {
                    var (version, action) = await DeployAsync(ctx.Logger, ctx.CancellationToken);
                    ctx.ReportingStep.Log(LogLevel.Information, new MarkdownString($"Successfully reconciled **{Name}** as Foundry Toolbox (version {version}, action {action})"));
                },
                Tags = [WellKnownPipelineTags.DeployCompute],
                RequiredBySteps = [WellKnownPipelineSteps.Deploy],
                DependsOnSteps = [WellKnownPipelineSteps.DeployPrereq],
                Resource = this,
            },
        ])));
        Annotations.Add(new PipelineConfigurationAnnotation(context =>
        {
            var stack = Parent.Parent.Stack ?? context.Model.Resources.OfType<PulumiStackResource>().SingleOrDefault();
            if (stack is not null) context.GetSteps(this, WellKnownPipelineTags.DeployCompute).DependsOn(context.GetSteps(stack, WellKnownPipelineTags.ProvisionInfrastructure));
            return Task.CompletedTask;
        }));
    }

    public FoundryProjectResource Parent { get; }
    public string Description { get; set; } = "Foundry Toolbox";
    public string ApiVersion { get; set; } = "v1";
    public IDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The version that is the default after the toolbox has been deployed.</summary>
    public FoundryStaticValue DeployedVersion { get; } = new();

    internal IReadOnlyList<FoundryToolboxToolDefinition> Tools => _tools;

    /// <summary>The MCP endpoint of the toolbox (its default version).</summary>
    public ReferenceExpression UriExpression => ReferenceExpression.Create($"{Parent.Endpoint}/toolboxes/{Name}/mcp?api-version={ApiVersion}");

    public ReferenceExpression ConnectionStringExpression => UriExpression;

    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties()
    {
        yield return new("Name", ReferenceExpression.Create($"{Name}"));
        yield return new("ProjectEndpoint", ReferenceExpression.Create($"{Parent.Endpoint}"));
        yield return new("Uri", UriExpression);
        yield return new("ApiVersion", ReferenceExpression.Create($"{ApiVersion}"));
        yield return new("FoundryFeatures", ReferenceExpression.Create($"Toolboxes=V1Preview"));
        yield return new("AuthorizationScope", ReferenceExpression.Create($"https://ai.azure.com/.default"));
    }

    internal void AddTool(FoundryToolboxToolDefinition tool)
    {
        if (_tools.Any(t => string.Equals(t.Name, tool.Name, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Toolbox '{Name}' already contains a tool named '{tool.Name}'.");
        _tools.Add(tool);
    }

    internal async Task<(string Version, string Action)> DeployAsync(ILogger logger, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(await Parent.Endpoint.GetValueAsync(cancellationToken), UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"Foundry project '{Parent.Name}' did not resolve to an absolute HTTPS endpoint.");
        var options = new AIProjectClientOptions();
        options.AddPolicy(new FeaturesPolicy(), PipelinePosition.PerCall);
        var client = new AIProjectClient(new Uri(endpoint.GetLeftPart(UriPartial.Path).TrimEnd('/')), new DefaultAzureCredential(), options);
        var toolboxes = client.AgentAdministrationClient.GetAgentToolboxes();

        var tools = new List<ProjectsAgentTool>();
        foreach (var t in _tools) tools.Add(await t.ResolveAsync(cancellationToken));
        if (tools.Count == 0) throw new InvalidOperationException($"Toolbox '{Name}' must contain at least one tool.");
        var hash = ComputeHash(tools);
        var metadata = new Dictionary<string, string>(Metadata, StringComparer.Ordinal) { [ManagedByKey] = ManagedByValue, [HashKey] = hash, [SchemaKey] = "1" };

        async Task<ToolboxRecord?> GetAsync()
        {
            try { return (await toolboxes.GetToolboxAsync(Name, cancellationToken)).Value; }
            catch (ClientResultException e) when (e.Status == 404) { return null; }
        }

        async Task<ToolboxVersion> VersionAsync(string v) => (await toolboxes.GetToolboxVersionAsync(Name, v, cancellationToken)).Value;

        async Task<string> CreateAsync() =>
            (await toolboxes.CreateToolboxVersionAsync(Name, tools, Description, metadata, null, cancellationToken)).Value.Version;

        Task PromoteAsync(string v) => toolboxes.UpdateToolboxAsync(Name, v, new RequestOptions { CancellationToken = cancellationToken });

        var existing = await GetAsync();
        string version, action;
        if (existing is null)
        {
            version = await CreateAsync();
            await PromoteAsync(version);
            action = "CreatedAndPromoted";
        }
        else
        {
            var current = await VersionAsync(existing.DefaultVersion);
            if (!current.Metadata.TryGetValue(ManagedByKey, out var owner) || owner != ManagedByValue)
                throw new InvalidOperationException($"Toolbox '{Name}' already exists but is not managed by Aspire. No changes were made.");
            if (current.Metadata.TryGetValue(HashKey, out var h) && h == hash)
            {
                version = existing.DefaultVersion;
                action = "Reused";
            }
            else
            {
                version = await CreateAsync();
                await PromoteAsync(version);
                action = "CreatedAndPromoted";
            }
        }
        logger.LogInformation("Reconciled Toolbox '{Toolbox}' at version {Version} ({Action}).", Name, version, action);
        DeployedVersion.Set(version);
        return (version, action);
    }

    private string ComputeHash(IReadOnlyList<ProjectsAgentTool> tools)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("description", Description);
            w.WriteStartObject("metadata");
            foreach (var (k, v) in Metadata.OrderBy(p => p.Key, StringComparer.Ordinal)) w.WriteString(k, v);
            w.WriteEndObject();
            w.WriteStartArray("tools");
            foreach (var json in tools.Select(t => ModelReaderWriter.Write(t, ModelReaderWriterOptions.Json, AzureAIProjectsAgentsContext.Default).ToString()).Order(StringComparer.Ordinal))
                w.WriteRawValue(json);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Convert.ToHexString(XxHash3.Hash(ms.ToArray())).ToLowerInvariant();
    }

    private sealed class FeaturesPolicy : PipelinePolicy
    {
        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Add("Foundry-Features", "Toolboxes=V1Preview");
            ProcessNext(message, pipeline, currentIndex);
        }

        public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Add("Foundry-Features", "Toolboxes=V1Preview");
            return ProcessNextAsync(message, pipeline, currentIndex);
        }
    }
}

internal static class FoundryToolbox
{
    internal static ProjectsAgentTool ReadTool(string json) =>
        ModelReaderWriter.Read<ProjectsAgentTool>(BinaryData.FromString(json), ModelReaderWriterOptions.Json, AzureAIProjectsAgentsContext.Default)!;
}
