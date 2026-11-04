using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Sheridan.Aspire.Pulumi.Azure.Base;
using Sheridan.Aspire.Pulumi.Azure.Storage;

namespace Sheridan.Aspire.Pulumi.Azure.Functions;

/// <summary>
/// An Azure Functions project (the equivalent of Aspire.Hosting.Azure.AzureFunctionsProjectResource). It is a
/// <see cref="ProjectResource"/>, so it is deployed by the compute environment it targets (for example
/// <c>AddPulumiAzureContainerAppEnvironment</c>, which creates a container app with <c>kind: functionapp</c> so
/// KEDA scale rules follow the function triggers). This resource additionally owns the Functions host storage: the
/// compute environment's identity is granted Storage Blob/Queue/Table Data Contributor (by the Storage integration, because
/// the project references the account) plus Storage Account Contributor, as the official Bicep does.
/// </summary>
public class AzureFunctionsProjectResource(string name) : ProjectResource(name), IPulumiProgramContributor, IResourceWithCustomWithReference<AzureFunctionsProjectResource>
{
    public const string StorageAccountContributorRoleId = "17d1049b-9a84-46fb-8f53-869881c3d3ab";

    /// <summary>The storage account used for the Functions host (<c>AzureWebJobsStorage</c>).</summary>
    public AzureStorageResource? HostStorage { get; set; }

    /// <summary>Contributes only to the stack hosting the host storage (every stack when that is not bound yet).</summary>
    public PulumiStackResource? Stack => HostStorage?.Stack;

    public Task ContributeAsync(PulumiProgram program, PipelineStepContext context)
    {
        if (HostStorage is null) return Task.CompletedTask;

        var envs = Annotations.OfType<DeploymentTargetAnnotation>()
            .Select(a => a.ComputeEnvironment).OfType<IPulumiManagedIdentityResource>().Distinct();
        foreach (var env in envs)
        {
            var envId = PulumiAzureProgramExtensions.ToPulumiId(env.Name);
            program.AddRoleAssignment($"{PulumiAzureProgramExtensions.ToPulumiId(Name)}{envId}StorageAccountContributorRoleAssignment",
                env.PrincipalIdExpression, StorageAccountContributorRoleId, $"${{{HostStorage.Id}.id}}");
        }
        return Task.CompletedTask;
    }

    static IResourceBuilder<TDestination>? IResourceWithCustomWithReference<AzureFunctionsProjectResource>.TryWithReference<TDestination>(
        IResourceBuilder<TDestination> builder, IResourceBuilder<IResource> source, string? connectionName, bool optional, string? name) =>
        builder is IResourceBuilder<AzureFunctionsProjectResource> destination
            ? (IResourceBuilder<TDestination>?)(object?)AzureFunctionsProjectResourceExtensions.TryWithReference(destination, source, connectionName, optional, name)
            : null;
}
