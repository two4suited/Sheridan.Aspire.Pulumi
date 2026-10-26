using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace AspirePulumiPackages.Pulumi.Azure.Foundry;

/// <summary>Pulumi equivalents of the Aspire.Hosting.Foundry extension methods.</summary>
public static class FoundryExtensions
{
    /// <summary>Adds a Microsoft Foundry (AI Services) account provisioned by the Pulumi stack (the equivalent of AddFoundry).</summary>
    public static IResourceBuilder<FoundryResource> AddPulumiFoundry(this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new FoundryResource(name)).ExcludeFromManifest();
    }

    /// <summary>Alias of <see cref="AddPulumiFoundry"/> following the <c>AddPulumiAzureX</c> naming used by the other Azure integrations.</summary>
    public static IResourceBuilder<FoundryResource> AddPulumiAzureAIFoundry(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiFoundry(name);

    /// <summary>Adds a model deployment (the deployment name in Azure defaults to <paramref name="name"/>).</summary>
    public static IResourceBuilder<FoundryDeploymentResource> AddDeployment(
        this IResourceBuilder<FoundryResource> builder, string name, string modelName, string modelVersion, string format)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(modelName);
        ArgumentException.ThrowIfNullOrEmpty(modelVersion);
        ArgumentException.ThrowIfNullOrEmpty(format);
        var deployment = new FoundryDeploymentResource(name, modelName, modelVersion, format, builder.Resource);
        builder.Resource.Deployments.Add(deployment);
        return builder.ApplicationBuilder.AddResource(deployment).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>Adds a model deployment for a model of the catalog, e.g. <c>AddDeployment("chat", FoundryModel.OpenAI.Gpt4o)</c>.</summary>
    public static IResourceBuilder<FoundryDeploymentResource> AddDeployment(
        this IResourceBuilder<FoundryResource> builder, string name, FoundryModel model)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddDeployment(name, model.Name, model.Version, model.Format);
    }

    /// <summary>Uses an existing account instead of creating one (nothing is provisioned for the account itself; deployments, projects and role assignments are still applied). Mirrors <c>PublishAsExisting</c>.</summary>
    public static IResourceBuilder<FoundryResource> PublishAsExisting(
        this IResourceBuilder<FoundryResource> builder, string existingResourceName, string? resourceGroupName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(existingResourceName);
        ArgumentException.ThrowIfNullOrEmpty(resourceGroupName);
        builder.Resource.ExistingAccountName = existingResourceName;
        builder.Resource.ExistingResourceGroup = resourceGroupName;
        return builder;
    }

    /// <summary>Alias of <see cref="PublishAsExisting"/>. Mirrors <c>AsExisting</c>.</summary>
    public static IResourceBuilder<FoundryResource> AsExisting(
        this IResourceBuilder<FoundryResource> builder, string existingResourceName, string? resourceGroupName) =>
        builder.PublishAsExisting(existingResourceName, resourceGroupName);

    /// <summary>
    /// Runs the account against a Foundry Local service outside publish mode (connection string <c>Endpoint=&lt;endpoint&gt;;Key=unused</c>); in
    /// publish mode this is a no-op, exactly like the official extension. Unlike the official one, the Foundry Local service
    /// is not started and models are not downloaded for you: <paramref name="endpoint"/> (the OpenAI-compatible endpoint of a running
    /// service) is required.
    /// </summary>
    public static IResourceBuilder<FoundryResource> RunAsFoundryLocal(this IResourceBuilder<FoundryResource> builder, string? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.ApplicationBuilder.ExecutionContext.IsPublishMode) return builder;
        if (endpoint is null)
            throw new NotSupportedException("Managing the Foundry Local service (start, model download and load) is not ported; pass the endpoint of a running Foundry Local service, e.g. RunAsFoundryLocal(\"http://localhost:5272/v1/\").");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("The Foundry Local endpoint must be an absolute HTTP or HTTPS URL.", nameof(endpoint));
        if (!uri.AbsolutePath.EndsWith('/')) uri = new UriBuilder(uri) { Path = uri.AbsolutePath + "/" }.Uri;
        if (builder.Resource.Projects.Count > 0)
            throw new InvalidOperationException("Microsoft Foundry projects are not supported when the parent Foundry resource is configured with RunAsFoundryLocal().");
        builder.Resource.EmulatorServiceUri = uri;
        builder.Resource.ApiKey = "unused";
        return builder;
    }

    /// <summary>Configures the deployment (<c>DeploymentName</c>, <c>ModelName</c>, <c>ModelVersion</c>, <c>Format</c>, <c>SkuName</c>, <c>SkuCapacity</c>).</summary>
    public static IResourceBuilder<FoundryDeploymentResource> WithProperties(
        this IResourceBuilder<FoundryDeploymentResource> builder, Action<FoundryDeploymentResource> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        configure(builder.Resource);
        return builder;
    }

    /// <summary>Adds a Foundry project (<c>accounts/projects</c>) with its Application Insights component and connection, as <c>AddProject</c>.</summary>
    public static IResourceBuilder<FoundryProjectResource> AddProject(this IResourceBuilder<FoundryResource> builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (builder.Resource.IsEmulator)
            throw new InvalidOperationException("Microsoft Foundry projects are not supported when the parent Foundry resource is configured with RunAsFoundryLocal().");
        var project = new FoundryProjectResource(name, builder.Resource);
        builder.Resource.Projects.Add(project);
        return builder.ApplicationBuilder.AddResource(project).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>
    /// Replaces the default role assignments (Cognitive Services User and Cognitive Services OpenAI User) for the compute
    /// environment identity hosting <paramref name="builder"/> with the given roles on the account. Mirrors <c>WithRoleAssignments</c>.
    /// </summary>
    public static IResourceBuilder<T> WithRoleAssignments<T>(
        this IResourceBuilder<T> builder, IResourceBuilder<FoundryResource> target, params FoundryRole[] roles)
        where T : IResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(roles);
        foreach (var role in roles)
            if (!Enum.IsDefined(role)) throw new ArgumentException($"'{role}' is not a valid Foundry role.", nameof(roles));
        return builder.WithAnnotation(new FoundryRoleAnnotation(target.Resource, roles));
    }
}
