using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure.Search;

namespace Sheridan.Aspire.Pulumi.Azure.Foundry;

/// <summary>Pulumi equivalents of the toolbox extension methods of Aspire.Hosting.Foundry (<c>FoundryToolboxBuilderExtensions</c>).</summary>
public static class FoundryToolboxExtensions
{
    public static IResourceBuilder<FoundryToolboxResource> AddToolbox(
        this IResourceBuilder<FoundryProjectResource> project, string name, Action<FoundryToolboxResource>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var toolbox = new FoundryToolboxResource(name, project.Resource);
        configure?.Invoke(toolbox);
        return project.ApplicationBuilder.AddResource(toolbox).WithParentRelationship(project);
    }

    public static IResourceBuilder<FoundryToolboxResource> WithDescription(this IResourceBuilder<FoundryToolboxResource> builder, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        builder.Resource.Description = description;
        return builder;
    }

    public static IResourceBuilder<FoundryToolboxResource> WithWebSearchTool(
        this IResourceBuilder<FoundryToolboxResource> builder, string name = "web-search", string? description = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (description is not null) ArgumentException.ThrowIfNullOrWhiteSpace(description);
        builder.Resource.AddTool(new FoundryToolboxWebSearchToolDefinition(name, description));
        return builder;
    }

    public static IResourceBuilder<FoundryToolboxResource> WithMcpTool(
        this IResourceBuilder<FoundryToolboxResource> builder, string name, string endpoint, FoundryToolboxMcpToolOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || !FoundryToolboxMcpToolDefinition.IsFoundryReachableHttpsEndpoint(uri))
            throw new ArgumentException("The MCP endpoint must be a Foundry-reachable absolute HTTPS URI.", nameof(endpoint));
        return builder.WithMcpTool(name, ReferenceExpression.Create($"{uri.AbsoluteUri}"), options);
    }

    public static IResourceBuilder<FoundryToolboxResource> WithMcpTool(
        this IResourceBuilder<FoundryToolboxResource> builder, string name, EndpointReference endpoint, FoundryToolboxMcpToolOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return builder.WithMcpTool(name, ReferenceExpression.Create($"{endpoint}"), options);
    }

    public static IResourceBuilder<FoundryToolboxResource> WithMcpTool(
        this IResourceBuilder<FoundryToolboxResource> builder, string name, ReferenceExpression endpoint, FoundryToolboxMcpToolOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(endpoint);
        builder.Resource.AddTool(new FoundryToolboxMcpToolDefinition(name, endpoint, options));
        return builder;
    }

    /// <summary>Adds an AI Search tool; creates the project connection to the search resource (same stack) as <c>AddConnection(search)</c> does.</summary>
    public static IResourceBuilder<FoundryToolboxResource> WithAISearchTool(
        this IResourceBuilder<FoundryToolboxResource> builder, string name, IResourceBuilder<AzureSearchResource> search, string indexName)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        var connection = builder.ApplicationBuilder.CreateResourceBuilder(builder.Resource.Parent).AddConnection(search);
        builder.Resource.AddTool(new FoundryToolboxAISearchToolDefinition(name, connection.Resource, indexName));
        return builder;
    }
}
