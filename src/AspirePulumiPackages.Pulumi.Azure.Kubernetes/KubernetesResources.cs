using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Azure.Network;

namespace AspirePulumiPackages.Pulumi.Azure.Kubernetes;

// Pulumi counterparts of the resource types in the official Aspire.Hosting.Kubernetes / Aspire.Hosting.Azure.Kubernetes packages.
// They keep the upstream names and state so the AddIngress/AddGateway/AddHelmChart/AddCertManager/AddPersistentVolume/AddLoadBalancer
// call chains are the same; the AKS environment turns them into pulumi-kubernetes resources instead of Helm chart templates.

/// <summary>A string that is either a literal or the value of an Aspire parameter, resolved when the Pulumi program is generated.</summary>
public readonly record struct KubernetesValue
{
    private readonly object? _source;

    private KubernetesValue(object source) => _source = source;

    public static implicit operator KubernetesValue(string value) => new(value);

    public static KubernetesValue From(string value) => new(value);

    public static KubernetesValue From(ParameterResource parameter) => new(parameter);

    /// <summary>The literal text, or null when the value comes from a parameter.</summary>
    public string? Literal => _source as string;

    public async ValueTask<string> ResolveAsync(CancellationToken cancellationToken = default) => _source switch
    {
        string s => s,
        ParameterResource p => await p.GetValueAsync(cancellationToken) ?? throw new InvalidOperationException($"The parameter '{p.Name}' has no value."),
        _ => throw new InvalidOperationException("The value has not been set."),
    };
}

/// <summary>How an Ingress path is matched (<c>pathType</c>).</summary>
public enum IngressPathType { Prefix, Exact, ImplementationSpecific }

/// <summary>How a Gateway API HTTPRoute path is matched.</summary>
public enum GatewayPathMatchType { PathPrefix, Exact, RegularExpression }

/// <summary>PersistentVolumeClaim access modes.</summary>
public enum PersistentVolumeAccessMode { ReadWriteOnce, ReadOnlyMany, ReadWriteMany, ReadWriteOncePod }

public sealed record IngressPathConfig(string? Host, string Path, IngressPathType PathType, EndpointReference Endpoint);

public sealed record IngressTlsConfig(KubernetesValue SecretName);

public sealed record IngressDefaultBackendConfig(EndpointReference Endpoint);

public sealed record GatewayRouteConfig(string? Host, string Path, GatewayPathMatchType PathType, EndpointReference Endpoint);

public sealed record GatewayTlsConfig(KubernetesValue SecretName);

/// <summary>A Kubernetes <c>networking.k8s.io/v1 Ingress</c> routing external endpoints of the compute resources (see <c>AddIngress</c>).</summary>
public class KubernetesIngressResource(string name, AzureKubernetesEnvironmentResource environment)
    : Resource(name), IResourceWithParent<AzureKubernetesEnvironmentResource>
{
    public AzureKubernetesEnvironmentResource Parent { get; } = environment ?? throw new ArgumentNullException(nameof(environment));
    public KubernetesValue? IngressClassName { get; set; }
    public List<KubernetesValue> Hostnames { get; } = [];
    public List<IngressPathConfig> Paths { get; } = [];
    public List<IngressTlsConfig> TlsConfigs { get; } = [];
    public Dictionary<string, KubernetesValue> IngressAnnotations { get; } = [];
    public IngressDefaultBackendConfig? DefaultBackend { get; set; }

    /// <summary>An ingress without any path or default backend is not emitted.</summary>
    public bool ShouldMaterialize => Paths.Count > 0 || DefaultBackend is not null;
}

/// <summary>A Kubernetes Gateway API <c>Gateway</c> with its <c>HTTPRoute</c> (see <c>AddGateway</c>).</summary>
public class KubernetesGatewayResource(string name, AzureKubernetesEnvironmentResource environment)
    : Resource(name), IResourceWithParent<AzureKubernetesEnvironmentResource>
{
    public AzureKubernetesEnvironmentResource Parent { get; } = environment ?? throw new ArgumentNullException(nameof(environment));
    public KubernetesValue? GatewayClassName { get; set; }
    public List<KubernetesValue> Hostnames { get; } = [];
    public List<GatewayRouteConfig> Routes { get; } = [];
    public List<GatewayTlsConfig> TlsConfigs { get; } = [];
    public Dictionary<string, KubernetesValue> GatewayAnnotations { get; } = [];

    /// <summary>A gateway without routes is not emitted.</summary>
    public bool ShouldMaterialize => Routes.Count > 0;
}

/// <summary>An external Helm chart installed into the cluster as a release (see <c>AddHelmChart</c>).</summary>
public sealed class KubernetesHelmChartResource : Resource, IResourceWithParent<AzureKubernetesEnvironmentResource>
{
    public KubernetesHelmChartResource(string name, AzureKubernetesEnvironmentResource environment, string chartReference, string chartVersion) : base(name)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentException.ThrowIfNullOrEmpty(chartReference);
        ArgumentException.ThrowIfNullOrEmpty(chartVersion);
        Parent = environment;
        ChartReference = chartReference;
        ChartVersion = chartVersion;
    }

    public AzureKubernetesEnvironmentResource Parent { get; }
    public string ChartReference { get; }
    public string ChartVersion { get; }
    public string? Namespace { get; set; }
    public string? ReleaseName { get; set; }

    /// <summary>Helm values by dotted key (<c>helm --set</c> syntax); <c>true</c>/<c>false</c>/integers become typed values.</summary>
    public Dictionary<string, string> Values { get; } = [];

    /// <summary>Whether the release is removed when the stack is destroyed (see <c>WithDestroy</c>); otherwise it is retained.</summary>
    public bool DestroyOnUninstall { get; set; }

    /// <summary>Adopt resources that already exist in the cluster (see <c>WithForceConflicts</c>).</summary>
    public bool ForceConflicts { get; set; }
}

/// <summary>cert-manager installed from its Helm chart, hosting <c>ClusterIssuer</c>s (see <c>AddCertManager</c>).</summary>
public sealed class CertManagerResource : Resource, IResourceWithParent<AzureKubernetesEnvironmentResource>
{
    public CertManagerResource(string name, AzureKubernetesEnvironmentResource environment, KubernetesHelmChartResource helmChart) : base(name)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(helmChart);
        Parent = environment;
        HelmChart = helmChart;
    }

    public AzureKubernetesEnvironmentResource Parent { get; }
    public KubernetesHelmChartResource HelmChart { get; }
    public List<CertManagerIssuerResource> Issuers { get; } = [];
}

/// <summary>An ACME server and account of a cert-manager issuer.</summary>
public sealed record CertManagerAcmeIssuerSpec(KubernetesValue ServerUrl, KubernetesValue Email);

/// <summary>A cert-manager <c>ClusterIssuer</c> (see <c>AddIssuer</c>).</summary>
public sealed class CertManagerIssuerResource : Resource, IResourceWithParent<CertManagerResource>
{
    public CertManagerIssuerResource(string name, CertManagerResource parent) : base(name)
    {
        ArgumentNullException.ThrowIfNull(parent);
        Parent = parent;
    }

    public CertManagerResource Parent { get; }
    public CertManagerAcmeIssuerSpec? Spec { get; set; }

    /// <summary>The challenge solvers; only HTTP-01 (via the Gateway API) is supported, as upstream.</summary>
    public bool Http01Solver { get; set; }
}

/// <summary>A <c>PersistentVolumeClaim</c> workloads can mount (see <c>AddPersistentVolume</c>).</summary>
public sealed class KubernetesPersistentVolumeResource(string name, AzureKubernetesEnvironmentResource environment)
    : Resource(name), IResourceWithParent<AzureKubernetesEnvironmentResource>
{
    public AzureKubernetesEnvironmentResource Parent { get; } = environment ?? throw new ArgumentNullException(nameof(environment));
    public KubernetesValue? StorageClassName { get; set; }
    public KubernetesValue? Capacity { get; set; }
    public List<PersistentVolumeAccessMode> AccessModes { get; } = [];
    public Dictionary<string, KubernetesValue> VolumeAnnotations { get; } = [];

    /// <summary>The Kubernetes name of the claim.</summary>
    public string ClaimName => AzureKubernetesEnvironmentResource.ToKubernetesName(Name);
}

/// <summary>Mounts a <see cref="KubernetesPersistentVolumeResource"/> into a compute resource (see <c>WithPersistentVolume</c>).</summary>
/// <param name="MountPath">The container path; null binds the claim to the resource's own <c>WithVolume</c> mount of the same name.</param>
public sealed record KubernetesPersistentVolumeMountAnnotation(KubernetesPersistentVolumeResource Volume, string? MountPath, bool IsReadOnly) : IResourceAnnotation;

/// <summary>
/// An Azure Application Gateway for Containers (AGC) <c>ApplicationLoadBalancer</c> bound to a delegated subnet (see <c>AddLoadBalancer</c>).
/// Gateways and ingresses are associated with it via <c>WithLoadBalancer</c>.
/// </summary>
public sealed class AzureKubernetesLoadBalancerResource : Resource, IResourceWithParent<AzureKubernetesEnvironmentResource>
{
    internal const string AlbNamespace = "default";
    internal const string AlbGatewayClass = "azure-alb-external";

    public AzureKubernetesLoadBalancerResource(string name, AzureKubernetesEnvironmentResource parent, PulumiSubnetResource subnet) : base(name)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(subnet);
        Parent = parent;
        Subnet = subnet;
    }

    public AzureKubernetesEnvironmentResource Parent { get; }

    /// <summary>The delegated (<c>Microsoft.ServiceNetworking/trafficControllers</c>) subnet the load balancer associates with.</summary>
    public PulumiSubnetResource Subnet { get; }

    /// <summary>The in-cluster <c>ApplicationLoadBalancer</c> name referenced by the <c>alb.networking.azure.io/alb-name</c> annotation.</summary>
    public string AlbName => "alb-" + Name;
}
