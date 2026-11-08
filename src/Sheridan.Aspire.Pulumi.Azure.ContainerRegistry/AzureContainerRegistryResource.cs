using System.Text;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Sheridan.Aspire.Pulumi.Azure.Base;

namespace Sheridan.Aspire.Pulumi.Azure.ContainerRegistry;

/// <summary>A scheduled <c>acr purge</c> task (see <c>WithPurgeTask</c>).</summary>
public sealed record AzureContainerRegistryPurgeTask(string Schedule, string Filter, string Ago, int Keep);

/// <summary>Role assignments requested for a principal resource on a registry (see <c>WithRoleAssignments</c>).</summary>
public sealed class AzureContainerRegistryRoleAssignmentAnnotation(
    AzureContainerRegistryResource registry, AzureContainerRegistryRole[] roles) : IResourceAnnotation
{
    public AzureContainerRegistryResource Registry { get; } = registry;
    public AzureContainerRegistryRole[] Roles { get; } = roles;
}

/// <summary>
/// An Azure Container Registry provisioned by Pulumi (azure-native). Pulumi counterpart of the official
/// <c>AzureContainerRegistryResource</c>: Basic-SKU registry, optional purge tasks, and role assignments.
/// </summary>
public sealed class AzureContainerRegistryResource(string name) : PulumiAzureResource(name), IContainerRegistry
{
    public List<AzureContainerRegistryPurgeTask> PurgeTasks { get; } = [];

    public PulumiOutputReference NameOutputReference => Output(Stack ?? throw Unbound(), "Name");
    public PulumiOutputReference RegistryEndpoint => Output(Stack ?? throw Unbound(), "LoginServer");
    public PulumiOutputReference RegistryId => Output(Stack ?? throw Unbound(), "Id");

    ReferenceExpression IContainerRegistry.Name => ReferenceExpression.Create($"{NameOutputReference}");
    ReferenceExpression IContainerRegistry.Endpoint => ReferenceExpression.Create($"{RegistryEndpoint}");

    private InvalidOperationException Unbound() => new($"'{Name}' has not been bound to a Pulumi stack; call WithPulumiStack().");

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string resourceGroup, string location)
    {
        // Registry names are global and alphanumeric only: {project}{name}{stack}, shortened with a hash when too long.
        program.AddInfraResource(Id, "azure-native:containerregistry:Registry", new()
        {
            ["type"] = "azure-native:containerregistry:Registry",
            ["properties"] = new Dictionary<string, object?>
            {
                ["registryName"] = NameFor(AzureNameKind.ContainerRegistry),
                ["resourceGroupName"] = resourceGroup,
                ["location"] = location,
                ["sku"] = new Dictionary<string, object?> { ["name"] = "Basic" },
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
            },
        });

        for (var i = 0; i < PurgeTasks.Count; i++)
        {
            var t = PurgeTasks[i];
            var cmd = BuildPurgeTaskContent(t);
            program.AddInfraResource($"{Id}PurgeOldImages{i}", "azure-native:containerregistry:Task", new()
            {
                ["type"] = "azure-native:containerregistry:Task",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["taskName"] = PurgeTasks.Count == 1 ? "purgeOldImages" : $"purgeOldImages{i}",
                    ["registryName"] = $"${{{Id}.name}}",
                    ["resourceGroupName"] = resourceGroup,
                    ["location"] = location,
                    ["platform"] = new Dictionary<string, object?> { ["os"] = "Linux" },
                    ["step"] = new Dictionary<string, object?>
                    {
                        ["type"] = "EncodedTask",
                        ["encodedTaskContent"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(cmd)),
                    },
                    ["trigger"] = new Dictionary<string, object?>
                    {
                        ["timerTriggers"] = new List<object?>
                        {
                            new Dictionary<string, object?> { ["name"] = "purgeOldImages_trigger", ["schedule"] = t.Schedule },
                        },
                    },
                },
            });
        }

        AddRoleAssignments(program, context);

        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        program.Outputs[$"{Id}LoginServer"] = $"${{{Id}.loginServer}}";
        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";
        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The ACR task YAML for a purge task. Matching tags older than <c>Ago</c> are deleted while the newest <c>Keep</c> per
    /// repository are kept; eligible untagged manifests in matching repositories are always removed too (irreversible, and
    /// can break digest-based pulls), mirroring upstream Aspire.
    /// </summary>
    internal static string BuildPurgeTaskContent(AzureContainerRegistryPurgeTask t) =>
        $"version: v1.1.0\nsteps:\n- cmd: acr purge --filter '{t.Filter}' --ago {t.Ago} --keep {t.Keep} --untagged --max-untagged-manifests 0";

    private void AddRoleAssignments(PulumiProgram program, PipelineStepContext context)
    {
        foreach (var resource in context.Model.Resources)
        {
            foreach (var a in resource.Annotations.OfType<AzureContainerRegistryRoleAssignmentAnnotation>().Where(a => a.Registry == this))
            {
                var principalId = PrincipalIdExpression(resource);
                foreach (var role in a.Roles.Distinct())
                {
                    program.AddRoleAssignment($"{Id}{PulumiAzureProgramExtensions.ToPulumiId(resource.Name)}{role}", principalId,
                        role.RoleDefinitionId(), $"${{{Id}.id}}");
                }
            }
        }
    }

    private static string PrincipalIdExpression(IResource resource) => resource switch
    {
        PulumiAzureUserAssignedIdentityResource id => $"${{{id.Id}.principalId}}",
        IPulumiManagedIdentityResource managed => managed.PrincipalIdExpression,
        _ => throw new InvalidOperationException(
            $"Cannot assign registry roles to '{resource.Name}': it does not expose a Pulumi-managed identity " +
            "(use a Pulumi user-assigned identity or a Pulumi compute environment)."),
    };
}
