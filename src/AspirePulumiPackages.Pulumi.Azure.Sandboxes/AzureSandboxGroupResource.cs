using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using AspirePulumiPackages.Pulumi.Azure.Base;
using AspirePulumiPackages.Pulumi.Azure.ContainerRegistry;

namespace AspirePulumiPackages.Pulumi.Azure.Sandboxes;

/// <summary>
/// An Azure Container Apps sandbox group provisioned by Pulumi, mirroring the ARM part of
/// <c>AzureSandboxGroupResource</c> in Aspire.Hosting.Azure.Sandboxes: the sandbox group, the container registry
/// (<c>{name}-acr</c>, Basic) it pulls images from, a dedicated image-pull user-assigned identity (<c>{name}-mi</c>) with
/// <c>AcrPull</c> on the registry, and the <c>Container Apps SandboxGroup Data Owner</c> role for the deploying principal.
/// </summary>
/// <remarks>
/// Not ported: the data-plane deployment of compute resources (disk images, sandboxes, ports) that upstream performs
/// in-process, so this resource is not an <c>IComputeEnvironmentResource</c>.
/// </remarks>
public sealed class AzureSandboxGroupResource(string name) : PulumiAzureResource(name)
{
    // https://learn.microsoft.com/azure/role-based-access-control/built-in-roles#container-apps-sandboxgroup-data-owner
    internal const string SandboxGroupDataOwnerRoleId = "c24cf47c-5077-412d-a19c-45202126392c";

    // azure-native exposes the sandbox group under the containerinstance module (there is no azure-native:app token).
    internal const string SandboxGroupType = "azure-native:containerinstance/v20260701:SandboxGroup";

    internal bool SystemAssigned { get; set; }
    internal List<PulumiAzureUserAssignedIdentityResource> WorkloadIdentities { get; } = [];

    /// <summary>A registry to use instead of the group's own <c>{name}-acr</c> (see <c>WithAzureContainerRegistry</c>).</summary>
    public AzureContainerRegistryResource? Registry { get; internal set; }

    /// <summary>An image-pull identity to use instead of the group's own <c>{name}-mi</c> (see <c>WithAcrPullIdentity</c>).</summary>
    public PulumiAzureUserAssignedIdentityResource? PullIdentity { get; internal set; }

    internal string RegistryId => Registry?.Id ?? $"{Id}Acr";
    internal string PullIdentityId => PullIdentity?.Id ?? $"{Id}Mi";
    internal bool OwnsRegistry => Registry is null;
    internal bool OwnsPullIdentity => PullIdentity is null;

    private PulumiOutputReference Out(string suffix) =>
        Output(Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack()."), suffix);

    /// <summary>The sandbox group name.</summary>
    public PulumiOutputReference NameOutputReference => Out("Name");
    /// <summary>The sandbox group resource id.</summary>
    public PulumiOutputReference IdOutputReference => Out("Id");
    public PulumiOutputReference LocationOutputReference => Out("Location");
    /// <summary>The client id of the identity Azure Dev Compute uses to pull images.</summary>
    public PulumiOutputReference ImagePullIdentityClientId => Out("ImagePullIdentityClientId");

    /// <summary>The URL of the sandbox group's dashboard (requires the subscription id, so it is composed from outputs).</summary>
    public static string GetDashboardUrl(string subscriptionId, string resourceGroupName, string sandboxGroupName) =>
        $"https://sandboxes.azure.com/sandbox-groups/{Uri.EscapeDataString(subscriptionId)}/{Uri.EscapeDataString(resourceGroupName)}/{Uri.EscapeDataString(sandboxGroupName)}";

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        var stack = ResolveStack(context);
        RequireSameStack(Registry, stack, "container registry");
        RequireSameStack(PullIdentity, stack, "image-pull identity");
        foreach (var w in WorkloadIdentities) RequireSameStack(w, stack, "workload identity");
        if (PullIdentity is not null && WorkloadIdentities.Contains(PullIdentity))
            throw new InvalidOperationException(
                $"Sandbox group '{Name}' uses identity '{PullIdentity.Name}' for both image pulls and workloads. " +
                "Use a dedicated image-pull identity so its AcrPull permission is not exposed to sandbox workloads.");

        program.AddClientConfig();

        // Registry: the group's own '{name}-acr' (Basic) unless one was supplied.
        if (OwnsRegistry)
        {
            program.AddInfraResource(RegistryId, "azure-native:containerregistry:Registry", new()
            {
                ["type"] = "azure-native:containerregistry:Registry",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["location"] = location,
                    ["sku"] = new Dictionary<string, object?> { ["name"] = "Basic" },
                    ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = $"{Name}-acr" },
                },
            });
        }

        // Image-pull identity: the group's own '{name}-mi' unless one was supplied.
        if (OwnsPullIdentity)
        {
            program.AddInfraResource(PullIdentityId, "azure-native:managedidentity:UserAssignedIdentity", new()
            {
                ["type"] = "azure-native:managedidentity:UserAssignedIdentity",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["location"] = location,
                    ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = $"{Name}-mi" },
                },
            });
            // For a caller-supplied identity the caller remains responsible for AcrPull (as upstream).
            program.AddRoleAssignment($"{Id}AcrPull", $"${{{PullIdentityId}.principalId}}", AzureRoleDefinitions.AcrPull,
                $"${{{RegistryId}.id}}");
        }

        var userAssigned = new List<object?> { $"${{{PullIdentityId}.id}}" };
        userAssigned.AddRange(WorkloadIdentities.Select(w => (object?)$"${{{w.Id}.id}}"));

        // No resourceName: the provider auto-names it, the equivalent of Bicep's take('<name>-${uniqueString(...)}', 63).
        program.AddInfraResource(Id, SandboxGroupType, new Dictionary<string, object?>
        {
            ["type"] = SandboxGroupType,
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = rg,
                ["location"] = location,
                ["identity"] = new Dictionary<string, object?>
                {
                    ["type"] = SystemAssigned ? "SystemAssigned, UserAssigned" : "UserAssigned",
                    ["userAssignedIdentities"] = userAssigned,
                },
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
            },
        });

        // Sandboxes, disk images and ports are created through the data plane after the group exists, using the deploying principal.
        program.AddRoleAssignment($"{Id}DeploymentPrincipalDataOwner", PulumiAzureProgramExtensions.CurrentPrincipalObjectId,
            SandboxGroupDataOwnerRoleId, $"${{{Id}.id}}", "User");

        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        program.Outputs[$"{Id}Location"] = $"${{{Id}.location}}";
        program.Outputs[$"{Id}ImagePullIdentityClientId"] = $"${{{PullIdentityId}.clientId}}";
        program.Outputs[$"{Id}RegistryLoginServer"] = $"${{{RegistryId}.loginServer}}";
        program.Outputs[$"{Id}RegistryName"] = $"${{{RegistryId}.name}}";

        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }

    private static void RequireSameStack(PulumiAzureResource? other, PulumiStackResource stack, string what)
    {
        if (other?.Stack is { } s && s != stack)
            throw new InvalidOperationException(
                $"The {what} '{other.Name}' is in Pulumi stack '{s.Name}', but the sandbox group is in '{stack.Name}'; they must share a stack.");
    }
}
