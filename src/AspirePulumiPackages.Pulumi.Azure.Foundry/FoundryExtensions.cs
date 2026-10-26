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
