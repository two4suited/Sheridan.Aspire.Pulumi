using System.Diagnostics;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Sheridan.Aspire.Pulumi.Azure;
using Sheridan.Aspire.Pulumi.Azure.Base;
using Sheridan.Aspire.Pulumi.Azure.ContainerRegistry;
using Sheridan.Aspire.Pulumi.Azure.Network;
using Sheridan.Aspire.Pulumi.Azure.OperationalInsights;
using Microsoft.Extensions.Logging;

namespace Sheridan.Aspire.Pulumi.Azure.Kubernetes;

/// <summary>Whether an AKS node pool hosts system pods or user workloads.</summary>
public enum AksNodePoolMode { System, User }

/// <summary>Size and autoscaling configuration of an AKS node pool.</summary>
public sealed record AksNodePoolConfig(string Name, string VmSize, int MinCount, int MaxCount, AksNodePoolMode Mode);

/// <summary>
/// Network profile of the cluster (see <c>WithNetworkProfile</c>). Upstream models this internally (defaults: Azure CNI, Calico,
/// 10.0.4.0/22, 10.0.4.10) without a public setter; here it is a typed option.
/// </summary>
public sealed record AksNetworkProfile(
    string NetworkPlugin = "azure", string? NetworkPolicy = "calico", string ServiceCidr = "10.0.4.0/22", string DnsServiceIP = "10.0.4.10");

/// <summary>A node pool of an AKS cluster (see <c>AddNodePool</c>); rendered inline in the cluster's <c>agentPoolProfiles</c>.</summary>
public sealed class AksNodePoolResource(string name, AksNodePoolConfig config, AzureKubernetesEnvironmentResource parent) : Resource(name)
{
    public AksNodePoolConfig Config { get; set; } = config;
    public AzureKubernetesEnvironmentResource Parent { get; } = parent;

    /// <summary>Per-pool subnet overriding the environment-level one (see <c>WithSubnet</c>).</summary>
    public PulumiSubnetResource? Subnet { get; set; }
}

/// <summary>Deployment target that represents the Kubernetes workload (ServiceAccount, Deployment/StatefulSet, Service) Pulumi creates for a compute resource.</summary>
public sealed class PulumiKubernetesWorkloadResource(string name, IResource targetResource) : Resource(name)
{
    public IResource TargetResource { get; } = targetResource;
}

/// <summary>
/// An Azure Kubernetes Service cluster provisioned by Pulumi (azure-native), and a compute environment that deploys onto it.
/// Pulumi counterpart of the official <c>AzureKubernetesEnvironmentResource</c>.
/// <para>
/// Infrastructure: resource group, managed cluster (system-assigned identity, autoscaled <c>system</c> and <c>workload</c> pools, OIDC issuer,
/// workload identity, optional typed network profile / private cluster), container registry with the AcrPull role for the kubelet identity,
/// a shared user-assigned workload identity with one federated credential per workload, and (for <c>AddLoadBalancer</c>) the AGC add-on.
/// </para>
/// <para>
/// Compute: compute resources that target it are built, pushed to the registry and deployed with the Pulumi Kubernetes provider as a
/// ServiceAccount, ConfigMap/Secret, Deployment (StatefulSet when volumes are mounted) and Service. Ingress, Gateway, Helm charts, cert-manager,
/// persistent volumes and load balancers added to it are emitted as pulumi-kubernetes resources too.
/// </para>
/// </summary>
#pragma warning disable ASPIRECOMPUTE002
public sealed partial class AzureKubernetesEnvironmentResource : PulumiAzureResource, IComputeEnvironmentResource, IContainerRegistry, IPulumiManagedIdentityResource
{
    internal const string AcrPullRoleId = "7f951dda-4ed3-4680-a7ca-43fe172d538d";
    internal const string NetworkContributorRoleId = "4d97b98b-1d4f-4787-a291-c67834d212e7";
    internal const string PrivateDnsZoneContributorRoleId = "b12aa53e-6015-4669-85d0-8515ebb3ae7f";
    internal const string DefaultVmSize = "Standard_D2s_v5";
    internal const string SystemPoolName = "system";
    internal const string DefaultUserPoolName = "workload";
    internal const string AlbPreviewResourceType = "azure-native:containerservice/v20250902preview:ManagedCluster";
    private const string AcrLoginTag = "acr-login";
    private const string AlbWaitTag = "aks-wait-alb";

    public AzureKubernetesEnvironmentResource(string name) : base(name)
    {
        Annotations.Add(new PipelineStepAnnotation(_ =>
        {
            var steps = new List<PipelineStep>
            {
                new()
                {
                    Name = $"pulumi-prepare-{name}",
                    Description = $"Targets compute resources at the AKS environment '{name}'",
                    Action = PrepareDeploymentTargetsAsync,
                    DependsOnSteps = [WellKnownPipelineSteps.ValidateComputeEnvironments],
                    RequiredBySteps = [WellKnownPipelineSteps.BeforeStart],
                },
                new()
                {
                    Name = $"acr-login-{name}",
                    Description = $"Log the container runtime in to the registry of '{name}'",
                    Tags = [AcrLoginTag],
                    Action = LoginToRegistryAsync,
                },
            };
            if (LoadBalancers.Count > 0)
            {
                steps.Add(new PipelineStep
                {
                    Name = $"aks-wait-alb-{name}",
                    Description = $"Waits for the Application Gateway for Containers controller of '{name}' to register its GatewayClass",
                    Tags = [AlbWaitTag],
                    Action = WaitForAlbControllerAsync,
                });
            }
            return (IEnumerable<PipelineStep>)steps;
        }));

        Annotations.Add(new PipelineConfigurationAnnotation(context =>
        {
            var stack = Stack ?? context.Model.Resources.OfType<PulumiStackResource>().SingleOrDefault();
            if (stack is null) return;

            var provision = context.GetSteps(stack, WellKnownPipelineTags.ProvisionInfrastructure);
            var deploy = context.GetSteps(stack, WellKnownPipelineTags.DeployCompute);
            var login = context.GetSteps(this, AcrLoginTag);
            login.DependsOn(provision);

            if (LoadBalancers.Count > 0)
            {
                // The ALB controller add-on installs its CRDs and GatewayClass asynchronously after the cluster exists.
                var wait = context.GetSteps(this, AlbWaitTag);
                wait.DependsOn(provision);
                deploy.DependsOn(wait);
            }

            foreach (var r in context.Model.GetComputeResources().Where(IsTargeted))
            {
                var push = context.GetSteps(r, WellKnownPipelineTags.PushContainerImage);
                push.DependsOn(login);
                deploy.DependsOn(push);
            }

            foreach (var r in context.Model.GetBuildResources())
            {
                context.GetSteps(r, WellKnownPipelineTags.BuildCompute)
                    .RequiredBy(WellKnownPipelineSteps.Deploy)
                    .DependsOn(WellKnownPipelineSteps.DeployPrereq);
            }
        }));
    }

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

    /// <summary>The network profile (see <c>WithNetworkProfile</c>); when null the AKS default applies, or Azure CNI once a subnet is set (as upstream).</summary>
    public AksNetworkProfile? NetworkProfile { get; set; }

    /// <summary>Whether the API server is private (see <c>AsPrivateCluster</c>).</summary>
    public bool IsPrivateCluster { get; set; }

    /// <summary>A custom private DNS zone for a private cluster; null uses the AKS-managed (system) zone.</summary>
    public PulumiPrivateDnsZoneResource? PrivateDnsZone { get; set; }

    public List<KubernetesIngressResource> Ingresses { get; } = [];
    public List<KubernetesGatewayResource> Gateways { get; } = [];
    public List<KubernetesHelmChartResource> HelmCharts { get; } = [];
    public List<CertManagerResource> CertManagers { get; } = [];
    public List<KubernetesPersistentVolumeResource> PersistentVolumes { get; } = [];
    public List<AzureKubernetesLoadBalancerResource> LoadBalancers { get; } = [];

    public PulumiOutputReference IdOutputReference => Output(Stack ?? throw Unbound(), "Id");
    public PulumiOutputReference NameOutputReference => Output(Stack ?? throw Unbound(), "Name");
    public PulumiOutputReference ClusterFqdn => Output(Stack ?? throw Unbound(), "ClusterFqdn");
    public PulumiOutputReference OidcIssuerUrl => Output(Stack ?? throw Unbound(), "OidcIssuerUrl");
    public PulumiOutputReference KubeletIdentityObjectId => Output(Stack ?? throw Unbound(), "KubeletIdentityObjectId");
    public PulumiOutputReference NodeResourceGroup => Output(Stack ?? throw Unbound(), "NodeResourceGroup");
    public PulumiOutputReference RegistryName => Output(Stack ?? throw Unbound(), "RegistryName");
    public PulumiOutputReference RegistryEndpoint => Output(Stack ?? throw Unbound(), "RegistryEndpoint");

    private InvalidOperationException Unbound() => new($"'{Name}' has not been bound to a Pulumi stack; call WithPulumiStack().");

    private PulumiStackResource? _resolvedStack;

    private PulumiOutputReference EnvOutput(string suffix) =>
        Output(_resolvedStack ?? Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet."), suffix);

    private string RegistryRef => Registry?.Id ?? $"{Id}Registry";

    private string IdentityRef => $"{Id}Identity";

    private bool IsTargeted(IResource r) =>
        r.Annotations.OfType<DeploymentTargetAnnotation>().Any(a => a.ComputeEnvironment == this);

    // The shared workload identity: every workload runs as it (one federated credential per Kubernetes service account), so integrations
    // that grant roles to a compute environment's identity (IPulumiManagedIdentityResource) work unchanged.
    string IPulumiManagedIdentityResource.PrincipalIdExpression => $"${{{WorkloadIdentityRef()}.principalId}}";

    string? IPulumiManagedIdentityResource.PrincipalNameExpression => $"${{{WorkloadIdentityRef()}.name}}";

    string? IPulumiManagedIdentityResource.ClientIdExpression => $"${{{WorkloadIdentityRef()}.clientId}}";

    private string WorkloadIdentityRef() => WorkloadIdentityEnabled
        ? IdentityRef
        : throw new InvalidOperationException(
            $"Workload identity is disabled on '{Name}', so its workloads have no Azure identity to grant roles to. Call WithWorkloadIdentity() to enable it.");

    ReferenceExpression IContainerRegistry.Name => ReferenceExpression.Create($"{EnvOutput("RegistryName")}");

    ReferenceExpression IContainerRegistry.Endpoint => ReferenceExpression.Create($"{EnvOutput("RegistryEndpoint")}");

    ReferenceExpression? IContainerRegistry.Repository => null;

    /// <summary>A workload is reached through its in-cluster Service, <c>{name}-service</c>.</summary>
    public ReferenceExpression GetHostAddressExpression(EndpointReference endpointReference) =>
        ReferenceExpression.Create($"{ServiceName(endpointReference.Resource.Name)}");

    public ReferenceExpression GetEndpointPropertyExpression(EndpointReferenceExpression endpointReferenceExpression)
    {
        var endpoint = endpointReferenceExpression.Endpoint;
        var host = ServiceName(endpoint.Resource.Name);
        var port = ContainerPort(endpoint.EndpointAnnotation).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var scheme = endpoint.EndpointAnnotation.UriScheme;
        return endpointReferenceExpression.Property switch
        {
            EndpointProperty.Url => ReferenceExpression.Create($"{scheme}://{host}:{port}"),
            EndpointProperty.Host or EndpointProperty.IPV4Host => ReferenceExpression.Create($"{host}"),
            EndpointProperty.Port or EndpointProperty.TargetPort => ReferenceExpression.Create($"{port}"),
            EndpointProperty.Scheme => ReferenceExpression.Create($"{scheme}"),
            EndpointProperty.HostAndPort => ReferenceExpression.Create($"{host}:{port}"),
            _ => ReferenceExpression.Create($"{host}"),
        };
    }

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

    private async Task LoginToRegistryAsync(PipelineStepContext ctx)
    {
        var registry = await EnvOutput("RegistryName").GetValueAsync(ctx.CancellationToken);
        var subscription = await EnvOutput("SubscriptionId").GetValueAsync(ctx.CancellationToken);
        var (exit, output) = await RunAsync("az", ["acr", "login", "--name", registry!, "--subscription", subscription!], ctx.CancellationToken);
        if (exit != 0)
            throw new InvalidOperationException($"'az acr login' failed (run 'az login' first): {output}");
        ctx.Logger.LogInformation("Logged in to registry {Registry}", registry);
    }

    /// <summary>
    /// Polls the cluster for the <c>azure-alb-external</c> GatewayClass the AGC controller add-on installs (as upstream's
    /// <c>apply-alb-crd</c> step does) so the ApplicationLoadBalancer custom resource is applied after its CRD exists.
    /// </summary>
    private async Task WaitForAlbControllerAsync(PipelineStepContext ctx)
    {
        _resolvedStack = ResolveStack(ctx);
        var cluster = await EnvOutput("Name").GetValueAsync(ctx.CancellationToken);
        var group = await EnvOutput("ResourceGroup").GetValueAsync(ctx.CancellationToken);
        var subscription = await EnvOutput("SubscriptionId").GetValueAsync(ctx.CancellationToken);

        var kubeconfig = Path.Combine(Directory.CreateTempSubdirectory("aspire-aks").FullName, "kubeconfig");
        try
        {
            var (exit, output) = await RunAsync("az",
                ["aks", "get-credentials", "--resource-group", group!, "--name", cluster!, "--subscription", subscription!, "--file", kubeconfig, "--overwrite-existing"],
                ctx.CancellationToken);
            if (exit != 0)
            {
                ctx.Logger.LogWarning("Could not fetch AKS credentials to wait for the ALB controller (the deploy may need a retry): {Output}", output);
                return;
            }

            var deadline = DateTime.UtcNow.AddMinutes(10);
            while (true)
            {
                int code;
                try
                {
                    (code, _) = await RunAsync("kubectl", ["--kubeconfig", kubeconfig, "get", "gatewayclass", AzureKubernetesLoadBalancerResource.AlbGatewayClass], ctx.CancellationToken);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    ctx.Logger.LogWarning("kubectl is not installed; not waiting for the ALB controller (the deploy may need a retry).");
                    return;
                }

                if (code == 0) break;
                if (DateTime.UtcNow > deadline)
                {
                    ctx.Logger.LogWarning("Timed out waiting for GatewayClass '{Class}'; continuing (the deploy may need a retry).", AzureKubernetesLoadBalancerResource.AlbGatewayClass);
                    return;
                }
                await Task.Delay(TimeSpan.FromSeconds(10), ctx.CancellationToken);
            }

            ctx.Logger.LogInformation("The ALB controller is ready.");
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(kubeconfig)!, recursive: true); } catch (IOException) { }
        }
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string file, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start '{file}'. Is it installed?");
        var output = await process.StandardOutput.ReadToEndAsync(ct) + await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return (process.ExitCode, output);
    }

    private Task PrepareDeploymentTargetsAsync(PipelineStepContext ctx)
    {
        if (!ctx.ExecutionContext.IsPublishMode) return Task.CompletedTask;

        _resolvedStack = ResolveStack(ctx);

        foreach (var r in ctx.Model.GetComputeResources())
        {
            var explicitEnv = r.GetComputeEnvironment();
            if (explicitEnv is not null && explicitEnv != this) continue;
            if (IsTargeted(r)) continue;

            r.Annotations.Add(new DeploymentTargetAnnotation(new PulumiKubernetesWorkloadResource(r.Name, r))
            {
                ContainerRegistry = this,
                ComputeEnvironment = this,
            });

#pragma warning disable ASPIREPIPELINES003
            r.Annotations.Add(new ContainerImagePushOptionsCallbackAnnotation(c =>
            {
                c.Options.RemoteImageName = r.Name.ToLowerInvariant();
                c.Options.RemoteImageTag = _resolvedStack.DeployTag;
            }));
#pragma warning restore ASPIREPIPELINES003
        }

        return Task.CompletedTask;
    }

    protected override async Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext ctx, string rg, string location)
    {
        var stack = _resolvedStack ?? ResolveStack(ctx);
        RequireSameStack(Registry, stack, "container registry");
        RequireSameStack(LogAnalyticsWorkspace, stack, "Log Analytics workspace");
        RequireSameStack(PrivateDnsZone, stack, "private DNS zone");

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
                    ["registryName"] = NameFor(AzureNameKind.ContainerRegistry, "acr"),
                    ["resourceGroupName"] = rg,
                    ["location"] = location,
                    ["sku"] = new Dictionary<string, object?> { ["name"] = "Basic" },
                    ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = $"{Name}-acr" },
                },
            });
        }
        else registryRef = Registry.Id;

        var targeted = ctx.Model.GetComputeResources().Where(IsTargeted).ToList();
        var identity = new Dictionary<string, object?> { ["type"] = "SystemAssigned" };
        var clusterDependsOn = new List<object?>();

        // Private cluster with a custom private DNS zone: the control plane needs a user-assigned identity that can manage the zone.
        if (IsPrivateCluster && PrivateDnsZone is { } zone)
        {
            var cp = $"{Id}ControlPlaneIdentity";
            program.AddUserAssignedIdentity(cp, rg, location, NameFor(AzureNameKind.UserAssignedIdentity, "cp-mi"));
            clusterDependsOn.Add($"${{{program.AddRoleAssignment($"{Id}DnsZoneContributor", $"${{{cp}.principalId}}", PrivateDnsZoneContributorRoleId, zone.IdExpression)}}}");
            clusterDependsOn.Add($"${{{program.AddRoleAssignment($"{Id}VnetNetworkContributor", $"${{{cp}.principalId}}", NetworkContributorRoleId, zone.VirtualNetwork.IdExpression)}}}");
            identity = new Dictionary<string, object?>
            {
                ["type"] = "UserAssigned",
                ["userAssignedIdentities"] = new List<object?> { $"${{{cp}.id}}" },
            };
        }

        var pools = EffectivePools().Select(p => (object?)PoolProfile(p.Config, p.Subnet ?? Subnet)).ToList();
        var properties = new Dictionary<string, object?>
        {
            ["resourceName"] = NameFor(AzureNameKind.KubernetesCluster),
            ["resourceGroupName"] = rg,
            ["location"] = location,
            ["dnsPrefix"] = NameFor(AzureNameKind.AksDnsPrefix),
            ["agentPoolProfiles"] = pools,
            ["sku"] = new Dictionary<string, object?> { ["name"] = "Base", ["tier"] = "Free" },
            ["identity"] = identity,
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

        if (IsPrivateCluster)
        {
            var access = new Dictionary<string, object?> { ["enablePrivateCluster"] = true };
            if (PrivateDnsZone is { } dns) access["privateDNSZone"] = dns.IdExpression;
            properties["apiServerAccessProfile"] = access;
        }

        // Network profile: the explicit typed one, else Azure CNI as soon as a node subnet is used (upstream's implicit default).
        var usesSubnet = Subnet is not null || NodePools.Any(p => p.Subnet is not null);
        if (NetworkProfile is { } np)
        {
            var network = new Dictionary<string, object?>
            {
                ["networkPlugin"] = np.NetworkPlugin,
                ["serviceCidr"] = np.ServiceCidr,
                ["dnsServiceIP"] = np.DnsServiceIP,
            };
            if (np.NetworkPolicy is not null) network["networkPolicy"] = np.NetworkPolicy;
            properties["networkProfile"] = network;
        }
        else if (usesSubnet)
        {
            properties["networkProfile"] = new Dictionary<string, object?> { ["networkPlugin"] = "azure" };
        }

        // AGC needs the managed Gateway API and the ALB controller add-on, which only exist in preview AKS API versions (as upstream's 2025-09-02-preview).
        var clusterType = "azure-native:containerservice:ManagedCluster";
        if (LoadBalancers.Count > 0)
        {
            clusterType = AlbPreviewResourceType;
            properties["ingressProfile"] = new Dictionary<string, object?>
            {
                ["gatewayAPI"] = new Dictionary<string, object?> { ["installation"] = "Standard" },
                ["applicationLoadBalancer"] = new Dictionary<string, object?> { ["enabled"] = true },
            };
        }

        var cluster = new Dictionary<string, object?> { ["type"] = clusterType, ["properties"] = properties };
        if (clusterDependsOn.Count > 0) cluster["options"] = new Dictionary<string, object?> { ["dependsOn"] = clusterDependsOn };
        program.AddInfraResource(Id, clusterType, cluster);

        // Kubelet identity pulls images from the registry (official: acrPullRole scoped to the registry).
        var kubelet = $"${{{Id}.identityProfile[\"kubeletidentity\"].objectId}}";
        program.AddRoleAssignment($"{Id}AcrPull", kubelet, AcrPullRoleId, $"${{{registryRef}.id}}");

        // The AGC controller's identity joins the load balancers' (delegated) subnets (upstream: a Network Contributor assignment per subnet).
        var albJoins = new List<string>();
        foreach (var subnet in LoadBalancers.Select(l => l.Subnet).Distinct())
        {
            albJoins.Add(program.AddRoleAssignment($"{Id}AlbSubnetJoin{subnet.PulumiId}",
                $"${{{Id}.ingressProfile.applicationLoadBalancer.identity.objectId}}", NetworkContributorRoleId, subnet.IdExpression));
        }

        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        program.Outputs[$"{Id}ClusterFqdn"] = $"${{{Id}.fqdn}}";
        if (oidc) program.Outputs[$"{Id}OidcIssuerUrl"] = $"${{{Id}.oidcIssuerProfile.issuerURL}}";
        program.Outputs[$"{Id}KubeletIdentityObjectId"] = kubelet;
        program.Outputs[$"{Id}NodeResourceGroup"] = $"${{{Id}.nodeResourceGroup}}";
        program.Outputs[$"{Id}ResourceGroup"] = rg;
        program.Outputs[$"{Id}SubscriptionId"] = PulumiAzureProgramExtensions.SubscriptionId;
        program.Outputs[$"{Id}RegistryName"] = $"${{{registryRef}.name}}";
        program.Outputs[$"{Id}RegistryEndpoint"] = $"${{{registryRef}.loginServer}}";

        await ContributeKubernetesAsync(program, ctx, stack, rg, location, registryRef, targeted, albJoins);

        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
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

    /// <summary>Converts an Aspire resource name to a DNS-1123 label (lower case, <c>[a-z0-9-]</c>, at most 63 characters).</summary>
    internal static string ToKubernetesName(string name, int reserve = 0)
    {
        var max = 63 - reserve;
        var chars = name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        var s = new string(chars).Trim('-');
        if (s.Length == 0) return "res";
        if (s.Length <= max) return s;
        // Too long: trim and append a short deterministic hash of the full name so different long names never collide.
        var hash = AzureNaming.StableHash([name], AzureNameKind.KubernetesCluster);
        return s[..(max - hash.Length - 1)].TrimEnd('-') + "-" + hash;
    }

    private const string ServiceSuffix = "-service";

    internal static string ServiceName(string resourceName) => ToKubernetesName(resourceName, ServiceSuffix.Length) + ServiceSuffix;

    /// <summary>The container (and Service) port of an endpoint.</summary>
    internal static int ContainerPort(EndpointAnnotation e) => e.TargetPort ?? e.Port ?? 8080;
}
