using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using AspirePulumiPackages.Pulumi.Azure.Base;
using AspirePulumiPackages.Pulumi.Azure.ContainerRegistry;
using AspirePulumiPackages.Pulumi.Azure.Network;
using AspirePulumiPackages.Pulumi.Azure.OperationalInsights;

namespace AspirePulumiPackages.Pulumi.Azure.Kubernetes;

/// <summary>Whether an AKS node pool hosts system pods or user workloads.</summary>
public enum AksNodePoolMode { System, User }

/// <summary>Size and autoscaling configuration of an AKS node pool.</summary>
public sealed record AksNodePoolConfig(string Name, string VmSize, int MinCount, int MaxCount, AksNodePoolMode Mode);

/// <summary>A node pool of an AKS cluster (see <c>AddNodePool</c>); rendered inline in the cluster's <c>agentPoolProfiles</c>.</summary>
public sealed class AksNodePoolResource(string name, AksNodePoolConfig config, AzureKubernetesEnvironmentResource parent) : Resource(name)
{
    public AksNodePoolConfig Config { get; set; } = config;
    public AzureKubernetesEnvironmentResource Parent { get; } = parent;

    /// <summary>Per-pool subnet overriding the environment-level one (see <c>WithSubnet</c>).</summary>
    public PulumiSubnetResource? Subnet { get; set; }
}

/// <summary>
/// An Azure Kubernetes Service cluster provisioned by Pulumi (azure-native). Pulumi counterpart of the official
/// <c>AzureKubernetesEnvironmentResource</c>: resource group, managed cluster (system-assigned identity, autoscaled
/// <c>system</c> and <c>workload</c> pools, OIDC issuer, workload identity), container registry and the AcrPull role
/// for the kubelet identity. Helm/Kubernetes deployment of compute resources is not part of this resource.
/// </summary>
public sealed class AzureKubernetesEnvironmentResource(string name) : PulumiAzureResource(name)
{
    internal const string AcrPullRoleId = "7f951dda-4ed3-4680-a7ca-43fe172d538d";
    internal const string DefaultVmSize = "Standard_D2s_v5";
    internal const string SystemPoolName = "system";
    internal const string DefaultUserPoolName = "workload";

    /// <summary>The Kubernetes version; when null AKS picks its default.</summary>
    public string? KubernetesVersion { get; set; }

    public bool OidcIssuerEnabled { get; set; } = true;
    public bool WorkloadIdentityEnabled { get; set; } = true;

    /// <summary>The system pool (see <c>WithSystemNodePool</c>).</summary>
    public AksNodePoolConfig SystemNodePool { get; set; } = new(SystemPoolName, DefaultVmSize, 1, 3, AksNodePoolMode.System);

    /// <summary>User node pools added with <c>AddNodePool</c>; a default <c>workload</c> pool is used when there are none.</summary>
    public List<AksNodePoolResource> NodePools { get; } = [];

    /// <summary>A registry to use instead of the environment's own (see <c>WithContainerRegistry</c>).</summary>
    public AzureContainerRegistryResource? Registry { get; set; }

    /// <summary>A Log Analytics workspace enabling the Container Insights add-on (see <c>WithContainerInsights</c>).</summary>
    public LogAnalyticsWorkspaceResource? LogAnalyticsWorkspace { get; set; }

    /// <summary>Environment-level node subnet (see <c>WithSubnet</c>), used by every pool without its own.</summary>
    public PulumiSubnetResource? Subnet { get; set; }

    public PulumiOutputReference IdOutputReference => Output(Stack ?? throw Unbound(), "Id");
    public PulumiOutputReference NameOutputReference => Output(Stack ?? throw Unbound(), "Name");
    public PulumiOutputReference ClusterFqdn => Output(Stack ?? throw Unbound(), "ClusterFqdn");
    public PulumiOutputReference OidcIssuerUrl => Output(Stack ?? throw Unbound(), "OidcIssuerUrl");
    public PulumiOutputReference KubeletIdentityObjectId => Output(Stack ?? throw Unbound(), "KubeletIdentityObjectId");
    public PulumiOutputReference NodeResourceGroup => Output(Stack ?? throw Unbound(), "NodeResourceGroup");
    public PulumiOutputReference RegistryName => Output(Stack ?? throw Unbound(), "RegistryName");
    public PulumiOutputReference RegistryEndpoint => Output(Stack ?? throw Unbound(), "RegistryEndpoint");

    private InvalidOperationException Unbound() => new($"'{Name}' has not been bound to a Pulumi stack; call WithPulumiStack().");

    /// <summary>System pool followed by the user pools (a default <c>workload</c> pool when none was added).</summary>
    internal IReadOnlyList<(AksNodePoolConfig Config, PulumiSubnetResource? Subnet)> EffectivePools()
    {
        var pools = new List<(AksNodePoolConfig, PulumiSubnetResource?)> { (SystemNodePool, null) };
        if (NodePools.Count == 0)
            pools.Add((new AksNodePoolConfig(DefaultUserPoolName, DefaultVmSize, 1, 3, AksNodePoolMode.User), null));
        else
            pools.AddRange(NodePools.Select(p => (p.Config, p.Subnet)));
        return pools;
    }

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext ctx, string rg, string location)
    {
        var stack = ResolveStack(ctx);
        RequireSameStack(Registry, stack, "container registry");
        RequireSameStack(LogAnalyticsWorkspace, stack, "Log Analytics workspace");

        program.AddClientConfig();

        // Container registry: the environment's own (upstream '{name}-acr') unless one was supplied.
        string registryRef;
        if (Registry is null)
        {
            registryRef = $"{Id}Registry";
            program.AddInfraResource(registryRef, "azure-native:containerregistry:Registry", new()
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
        else registryRef = Registry.Id;

        var pools = EffectivePools().Select(p => (object?)PoolProfile(p.Config, p.Subnet ?? Subnet)).ToList();
        var properties = new Dictionary<string, object?>
        {
            ["resourceGroupName"] = rg,
            ["location"] = location,
            ["dnsPrefix"] = $"{Name}-dns",
            ["agentPoolProfiles"] = pools,
            ["sku"] = new Dictionary<string, object?> { ["name"] = "Base", ["tier"] = "Free" },
            ["identity"] = new Dictionary<string, object?> { ["type"] = "SystemAssigned" },
            ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
        };
        if (KubernetesVersion is not null) properties["kubernetesVersion"] = KubernetesVersion;
        var oidc = OidcIssuerEnabled || WorkloadIdentityEnabled;
        if (oidc) properties["oidcIssuerProfile"] = new Dictionary<string, object?> { ["enabled"] = true };
        if (WorkloadIdentityEnabled)
        {
            properties["securityProfile"] = new Dictionary<string, object?>
            {
                ["workloadIdentity"] = new Dictionary<string, object?> { ["enabled"] = true },
            };
        }
        if (LogAnalyticsWorkspace is { } law)
        {
            properties["addonProfiles"] = new Dictionary<string, object?>
            {
                ["omsagent"] = new Dictionary<string, object?>
                {
                    ["enabled"] = true,
                    ["config"] = new Dictionary<string, object?> { ["logAnalyticsWorkspaceResourceID"] = $"${{{law.Id}.id}}" },
                },
            };
        }

        program.AddInfraResource(Id, "azure-native:containerservice:ManagedCluster", new()
        {
            ["type"] = "azure-native:containerservice:ManagedCluster",
            ["properties"] = properties,
        });

        // Kubelet identity pulls images from the registry (official: acrPullRole scoped to the registry).
        var kubelet = $"${{{Id}.identityProfile[\"kubeletidentity\"].objectId}}";
        program.AddRoleAssignment($"{Id}AcrPull", kubelet, AcrPullRoleId, $"${{{registryRef}.id}}");

        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        program.Outputs[$"{Id}ClusterFqdn"] = $"${{{Id}.fqdn}}";
        if (oidc) program.Outputs[$"{Id}OidcIssuerUrl"] = $"${{{Id}.oidcIssuerProfile.issuerURL}}";
        program.Outputs[$"{Id}KubeletIdentityObjectId"] = kubelet;
        program.Outputs[$"{Id}NodeResourceGroup"] = $"${{{Id}.nodeResourceGroup}}";
        program.Outputs[$"{Id}RegistryName"] = $"${{{registryRef}.name}}";
        program.Outputs[$"{Id}RegistryEndpoint"] = $"${{{registryRef}.loginServer}}";

        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }

    private static Dictionary<string, object?> PoolProfile(AksNodePoolConfig c, PulumiSubnetResource? subnet)
    {
        var p = new Dictionary<string, object?>
        {
            ["name"] = c.Name,
            ["count"] = c.MinCount,
            ["vmSize"] = c.VmSize,
            ["osType"] = "Linux",
            ["maxCount"] = c.MaxCount,
            ["minCount"] = c.MinCount,
            ["enableAutoScaling"] = true,
            ["mode"] = c.Mode.ToString(),
        };
        if (subnet is not null) p["vnetSubnetID"] = subnet.IdExpression;
        return p;
    }

    private static void RequireSameStack(PulumiAzureResource? other, PulumiStackResource stack, string what)
    {
        if (other?.Stack is { } s && s != stack)
            throw new InvalidOperationException(
                $"The {what} '{other.Name}' is in Pulumi stack '{s.Name}', but the AKS environment is in '{stack.Name}'; they must share a stack.");
    }
}
