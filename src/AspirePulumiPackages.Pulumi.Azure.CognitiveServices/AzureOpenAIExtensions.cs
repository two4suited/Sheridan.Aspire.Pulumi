using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace AspirePulumiPackages.Pulumi.Azure.CognitiveServices;

/// <summary>Pulumi equivalents of the Aspire.Hosting.Azure.CognitiveServices (Azure OpenAI) extension methods.</summary>
public static class AzureOpenAIExtensions
{
    /// <summary>Adds an Azure OpenAI account provisioned by the Pulumi stack (the equivalent of AddAzureOpenAI).</summary>
    public static IResourceBuilder<AzureOpenAIResource> AddPulumiOpenAI(this IDistributedApplicationBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        return builder.AddResource(new AzureOpenAIResource(name)).ExcludeFromManifest();
    }

    /// <summary>Alias of <see cref="AddPulumiOpenAI"/> following the <c>AddPulumiAzureX</c> naming used by the other Azure integrations.</summary>
    public static IResourceBuilder<AzureOpenAIResource> AddPulumiAzureOpenAI(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiOpenAI(name);

    /// <summary>Alias of <see cref="AddPulumiOpenAI"/> named after the package (Aspire.Hosting.Azure.CognitiveServices).</summary>
    public static IResourceBuilder<AzureOpenAIResource> AddPulumiCognitiveServices(this IDistributedApplicationBuilder builder, string name) =>
        builder.AddPulumiOpenAI(name);

    /// <summary>Adds a model deployment to the account (the deployment name in Azure defaults to <paramref name="name"/>).</summary>
    public static IResourceBuilder<AzureOpenAIDeploymentResource> AddDeployment(
        this IResourceBuilder<AzureOpenAIResource> builder, string name, string modelName, string modelVersion)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(modelName);
        ArgumentException.ThrowIfNullOrEmpty(modelVersion);
        var deployment = new AzureOpenAIDeploymentResource(name, modelName, modelVersion, builder.Resource);
        builder.Resource.Deployments.Add(deployment);
        return builder.ApplicationBuilder.AddResource(deployment).WithParentRelationship(builder).ExcludeFromManifest();
    }

    /// <summary>Configures the deployment (<c>DeploymentName</c>, <c>ModelName</c>, <c>ModelVersion</c>, <c>SkuName</c>, <c>SkuCapacity</c>), as the official <c>WithProperties</c>.</summary>
    public static IResourceBuilder<AzureOpenAIDeploymentResource> WithProperties(
        this IResourceBuilder<AzureOpenAIDeploymentResource> builder, Action<AzureOpenAIDeploymentResource> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        configure(builder.Resource);
        return builder;
    }

    /// <summary>
    /// Replaces the default role assignment (Cognitive Services OpenAI User) for the compute environment identity hosting
    /// <paramref name="builder"/> with the given roles on the account. Mirrors <c>WithRoleAssignments</c>.
    /// </summary>
    public static IResourceBuilder<T> WithRoleAssignments<T>(
        this IResourceBuilder<T> builder, IResourceBuilder<AzureOpenAIResource> target, params AzureOpenAIRole[] roles)
        where T : IResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(roles);
        foreach (var role in roles)
            if (!Enum.IsDefined(role)) throw new ArgumentException($"'{role}' is not a valid Azure OpenAI role.", nameof(roles));
        return builder.WithAnnotation(new AzureOpenAIRoleAnnotation(target.Resource, roles));
    }
}
