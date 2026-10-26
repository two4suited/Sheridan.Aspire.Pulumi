using Aspire.Hosting.ApplicationModel;
using AspirePulumiPackages.Pulumi.Azure.Kubernetes;
using AspirePulumiPackages.Pulumi.Azure.Network;

namespace Aspire.Hosting;

// The compute-deployment half of Aspire.Hosting.Azure.Kubernetes / Aspire.Hosting.Kubernetes. Method names and signatures follow upstream
// (AddIngress, AddGateway, AddHelmChart, AddCertManager, AddPersistentVolume, AddLoadBalancer and the With* configuration methods); the
// resources they create are emitted by the AKS environment as pulumi-kubernetes resources.

/// <summary>Ingress, Gateway, Helm chart, cert-manager, persistent volume and load balancer additions for an AKS environment.</summary>
public static class AzureKubernetesIngressExtensions
{
    private static IResourceBuilder<T> Register<T>(IResourceBuilder<AzureKubernetesEnvironmentResource> aks, T resource, string icon) where T : Resource =>
        aks.ApplicationBuilder.AddResource(resource).WithParentRelationship(aks).WithIconName(icon).ExcludeFromManifest();

    /// <summary>Adds an Ingress. Mirrors <c>AddIngress</c>.</summary>
    public static IResourceBuilder<KubernetesIngressResource> AddIngress(this IResourceBuilder<AzureKubernetesEnvironmentResource> builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var ingress = new KubernetesIngressResource(name, builder.Resource);
        builder.Resource.Ingresses.Add(ingress);
        return Register(builder, ingress, "Globe");
    }

    /// <summary>Adds a Gateway API Gateway. Mirrors <c>AddGateway</c>.</summary>
    public static IResourceBuilder<KubernetesGatewayResource> AddGateway(this IResourceBuilder<AzureKubernetesEnvironmentResource> builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var gateway = new KubernetesGatewayResource(name, builder.Resource);
        builder.Resource.Gateways.Add(gateway);
        return Register(builder, gateway, "GlobeArrowForward");
    }

    /// <summary>Installs an external Helm chart as a Pulumi Helm release. Mirrors <c>AddHelmChart</c>.</summary>
    public static IResourceBuilder<KubernetesHelmChartResource> AddHelmChart(
        this IResourceBuilder<AzureKubernetesEnvironmentResource> builder, string name, string chartReference, string chartVersion)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var chart = new KubernetesHelmChartResource(name, builder.Resource, chartReference, chartVersion);
        builder.Resource.HelmCharts.Add(chart);
        return Register(builder, chart, "Box");
    }

    /// <summary>Installs cert-manager. Mirrors <c>AddCertManager</c> (default chart version v1.20.2).</summary>
    public static IResourceBuilder<CertManagerResource> AddCertManager(
        this IResourceBuilder<AzureKubernetesEnvironmentResource> builder, string name, string? chartVersion = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (name.Length > 64 - "-chart".Length)
            throw new ArgumentException($"cert-manager resource name '{name}' is too long (maximum {64 - "-chart".Length} characters).", nameof(name));

        var chart = new KubernetesHelmChartResource(name + "-chart", builder.Resource, "oci://quay.io/jetstack/charts/cert-manager", chartVersion ?? "v1.20.2");
        chart.Values["crds.enabled"] = "true";
        chart.Values["config.apiVersion"] = "controller.config.cert-manager.io/v1alpha1";
        chart.Values["config.kind"] = "ControllerConfiguration";
        chart.Values["config.enableGatewayAPI"] = "true";
        chart.Namespace = "cert-manager";
        chart.ForceConflicts = true;
        chart.DestroyOnUninstall = true;
        builder.Resource.HelmCharts.Add(chart);
        Register(builder, chart, "Box");

        var certManager = new CertManagerResource(name, builder.Resource, chart);
        builder.Resource.CertManagers.Add(certManager);
        return Register(builder, certManager, "Certificate");
    }

    /// <summary>Adds a PersistentVolumeClaim. Mirrors <c>AddPersistentVolume</c>.</summary>
    public static IResourceBuilder<KubernetesPersistentVolumeResource> AddPersistentVolume(
        this IResourceBuilder<AzureKubernetesEnvironmentResource> builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var volume = new KubernetesPersistentVolumeResource(name, builder.Resource);
        builder.Resource.PersistentVolumes.Add(volume);
        return Register(builder, volume, "Database");
    }

    /// <summary>
    /// Adds an Application Gateway for Containers load balancer on a subnet. Mirrors <c>AddLoadBalancer</c>: enables the managed Gateway API and
    /// the ALB controller add-on, delegates the subnet to <c>Microsoft.ServiceNetworking/trafficControllers</c>, grants the controller identity
    /// Network Contributor on it and creates the <c>ApplicationLoadBalancer</c> custom resource.
    /// </summary>
    public static IResourceBuilder<AzureKubernetesLoadBalancerResource> AddLoadBalancer(
        this IResourceBuilder<AzureKubernetesEnvironmentResource> builder, string name, IResourceBuilder<PulumiSubnetResource> subnet)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(subnet);

        const string service = "Microsoft.ServiceNetworking/trafficControllers";
        var delegations = subnet.Resource.Delegations;
        if (!delegations.Any(d => string.Equals(d.ServiceName, service, StringComparison.OrdinalIgnoreCase)))
        {
            // Upstream silently replaces a different delegation; the AGC subnet can only carry this one.
            delegations.Clear();
            delegations.Add((service, service));
        }

        var lb = new AzureKubernetesLoadBalancerResource(name, builder.Resource, subnet.Resource);
        builder.Resource.LoadBalancers.Add(lb);
        return Register(builder, lb, "GlobeArrowForward");
    }

    /// <summary>Routes a Gateway through an AGC load balancer. Mirrors <c>WithLoadBalancer</c>.</summary>
    public static IResourceBuilder<KubernetesGatewayResource> WithLoadBalancer(
        this IResourceBuilder<KubernetesGatewayResource> builder, IResourceBuilder<AzureKubernetesLoadBalancerResource> loadBalancer)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(loadBalancer);
        builder.Resource.GatewayAnnotations["alb.networking.azure.io/alb-name"] = loadBalancer.Resource.AlbName;
        builder.Resource.GatewayAnnotations["alb.networking.azure.io/alb-namespace"] = AzureKubernetesLoadBalancerResource.AlbNamespace;
        builder.Resource.GatewayClassName ??= AzureKubernetesLoadBalancerResource.AlbGatewayClass;
        return builder;
    }

    /// <summary>Routes an Ingress through an AGC load balancer. Mirrors <c>WithLoadBalancer</c>.</summary>
    public static IResourceBuilder<KubernetesIngressResource> WithLoadBalancer(
        this IResourceBuilder<KubernetesIngressResource> builder, IResourceBuilder<AzureKubernetesLoadBalancerResource> loadBalancer)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(loadBalancer);
        builder.Resource.IngressAnnotations["alb.networking.azure.io/alb-name"] = loadBalancer.Resource.AlbName;
        builder.Resource.IngressAnnotations["alb.networking.azure.io/alb-namespace"] = AzureKubernetesLoadBalancerResource.AlbNamespace;
        builder.Resource.IngressClassName ??= AzureKubernetesLoadBalancerResource.AlbGatewayClass;
        return builder;
    }
}

/// <summary>Configuration of <see cref="KubernetesIngressResource"/> (mirrors <c>KubernetesIngressExtensions</c>).</summary>
public static class PulumiKubernetesIngressExtensions
{
    public static IResourceBuilder<KubernetesIngressResource> WithIngressClass(this IResourceBuilder<KubernetesIngressResource> builder, string className)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        builder.Resource.IngressClassName = className;
        return builder;
    }

    public static IResourceBuilder<KubernetesIngressResource> WithIngressClass(this IResourceBuilder<KubernetesIngressResource> builder, IResourceBuilder<ParameterResource> className)
    {
        ArgumentNullException.ThrowIfNull(className);
        builder.Resource.IngressClassName = KubernetesValue.From(className.Resource);
        return builder;
    }

    public static IResourceBuilder<KubernetesIngressResource> WithPath(
        this IResourceBuilder<KubernetesIngressResource> builder, string path, EndpointReference endpoint, IngressPathType pathType = IngressPathType.Prefix)
    {
        ValidatePath(path, endpoint);
        builder.Resource.Paths.Add(new IngressPathConfig(null, path, pathType, endpoint));
        return builder;
    }

    public static IResourceBuilder<KubernetesIngressResource> WithPath(
        this IResourceBuilder<KubernetesIngressResource> builder, string host, string path, EndpointReference endpoint, IngressPathType pathType = IngressPathType.Prefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        ValidatePath(path, endpoint);
        builder.Resource.Paths.Add(new IngressPathConfig(host, path, pathType, endpoint));
        return builder;
    }

    public static IResourceBuilder<KubernetesIngressResource> WithHostname(this IResourceBuilder<KubernetesIngressResource> builder, string hostname)
    {
        ArgumentException.ThrowIfNullOrEmpty(hostname);
        builder.Resource.Hostnames.Add(hostname);
        return builder;
    }

    public static IResourceBuilder<KubernetesIngressResource> WithHostname(this IResourceBuilder<KubernetesIngressResource> builder, IResourceBuilder<ParameterResource> hostname)
    {
        ArgumentNullException.ThrowIfNull(hostname);
        builder.Resource.Hostnames.Add(KubernetesValue.From(hostname.Resource));
        return builder;
    }

    public static IResourceBuilder<KubernetesIngressResource> WithTls(this IResourceBuilder<KubernetesIngressResource> builder, string secretName)
    {
        ArgumentException.ThrowIfNullOrEmpty(secretName);
        builder.Resource.TlsConfigs.Add(new IngressTlsConfig(secretName));
        return builder;
    }

    public static IResourceBuilder<KubernetesIngressResource> WithTls(this IResourceBuilder<KubernetesIngressResource> builder, IResourceBuilder<ParameterResource> secretName)
    {
        ArgumentNullException.ThrowIfNull(secretName);
        builder.Resource.TlsConfigs.Add(new IngressTlsConfig(KubernetesValue.From(secretName.Resource)));
        return builder;
    }

    /// <summary>Terminates TLS with a secret named <c>{ingress}-tls</c> (as upstream).</summary>
    public static IResourceBuilder<KubernetesIngressResource> WithTls(this IResourceBuilder<KubernetesIngressResource> builder) =>
        builder.WithTls(builder.Resource.Name + "-tls");

    public static IResourceBuilder<KubernetesIngressResource> WithDefaultBackend(this IResourceBuilder<KubernetesIngressResource> builder, EndpointReference endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        builder.Resource.DefaultBackend = new IngressDefaultBackendConfig(endpoint);
        return builder;
    }

    public static IResourceBuilder<KubernetesIngressResource> WithIngressAnnotation(this IResourceBuilder<KubernetesIngressResource> builder, string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        builder.Resource.IngressAnnotations[key] = value;
        return builder;
    }

    public static IResourceBuilder<KubernetesIngressResource> WithIngressAnnotation(this IResourceBuilder<KubernetesIngressResource> builder, string key, IResourceBuilder<ParameterResource> value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        builder.Resource.IngressAnnotations[key] = KubernetesValue.From(value.Resource);
        return builder;
    }

    internal static void ValidatePath(string path, EndpointReference endpoint)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!path.StartsWith('/')) throw new ArgumentException("Path must start with '/'.", nameof(path));
    }
}

/// <summary>Configuration of <see cref="KubernetesGatewayResource"/> (mirrors <c>KubernetesGatewayExtensions</c>).</summary>
public static class PulumiKubernetesGatewayExtensions
{
    public static IResourceBuilder<KubernetesGatewayResource> WithGatewayClass(this IResourceBuilder<KubernetesGatewayResource> builder, string className)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        builder.Resource.GatewayClassName = className;
        return builder;
    }

    public static IResourceBuilder<KubernetesGatewayResource> WithGatewayClass(this IResourceBuilder<KubernetesGatewayResource> builder, IResourceBuilder<ParameterResource> className)
    {
        ArgumentNullException.ThrowIfNull(className);
        builder.Resource.GatewayClassName = KubernetesValue.From(className.Resource);
        return builder;
    }

    public static IResourceBuilder<KubernetesGatewayResource> WithRoute(
        this IResourceBuilder<KubernetesGatewayResource> builder, string path, EndpointReference endpoint, GatewayPathMatchType pathType = GatewayPathMatchType.PathPrefix)
    {
        PulumiKubernetesIngressExtensions.ValidatePath(path, endpoint);
        builder.Resource.Routes.Add(new GatewayRouteConfig(null, path, pathType, endpoint));
        return builder;
    }

    public static IResourceBuilder<KubernetesGatewayResource> WithRoute(
        this IResourceBuilder<KubernetesGatewayResource> builder, string host, string path, EndpointReference endpoint, GatewayPathMatchType pathType = GatewayPathMatchType.PathPrefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        PulumiKubernetesIngressExtensions.ValidatePath(path, endpoint);
        builder.Resource.Routes.Add(new GatewayRouteConfig(host, path, pathType, endpoint));
        return builder;
    }

    public static IResourceBuilder<KubernetesGatewayResource> WithHostname(this IResourceBuilder<KubernetesGatewayResource> builder, string hostname)
    {
        ArgumentException.ThrowIfNullOrEmpty(hostname);
        builder.Resource.Hostnames.Add(hostname);
        return builder;
    }

    public static IResourceBuilder<KubernetesGatewayResource> WithHostname(this IResourceBuilder<KubernetesGatewayResource> builder, IResourceBuilder<ParameterResource> hostname)
    {
        ArgumentNullException.ThrowIfNull(hostname);
        builder.Resource.Hostnames.Add(KubernetesValue.From(hostname.Resource));
        return builder;
    }

    public static IResourceBuilder<KubernetesGatewayResource> WithTls(this IResourceBuilder<KubernetesGatewayResource> builder, string secretName)
    {
        ArgumentException.ThrowIfNullOrEmpty(secretName);
        builder.Resource.TlsConfigs.Add(new GatewayTlsConfig(secretName));
        return builder;
    }

    public static IResourceBuilder<KubernetesGatewayResource> WithTls(this IResourceBuilder<KubernetesGatewayResource> builder, IResourceBuilder<ParameterResource> secretName)
    {
        ArgumentNullException.ThrowIfNull(secretName);
        builder.Resource.TlsConfigs.Add(new GatewayTlsConfig(KubernetesValue.From(secretName.Resource)));
        return builder;
    }

    /// <summary>Terminates TLS with a secret named <c>{gateway}-tls</c> (as upstream).</summary>
    public static IResourceBuilder<KubernetesGatewayResource> WithTls(this IResourceBuilder<KubernetesGatewayResource> builder) =>
        builder.WithTls(builder.Resource.Name + "-tls");

    /// <summary>Issues the gateway's certificate from a cert-manager issuer. Mirrors <c>WithTls(issuer)</c>.</summary>
    public static IResourceBuilder<KubernetesGatewayResource> WithTls(
        this IResourceBuilder<KubernetesGatewayResource> builder, IResourceBuilder<CertManagerIssuerResource> issuer)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        if (!string.Equals(builder.Resource.Parent.Name, issuer.Resource.Parent.Parent.Name, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Gateway '{builder.Resource.Name}' is in AKS environment '{builder.Resource.Parent.Name}' but issuer '{issuer.Resource.Name}' " +
                $"belongs to cert-manager '{issuer.Resource.Parent.Name}' in '{issuer.Resource.Parent.Parent.Name}'; cert-manager is per cluster.");
        return builder.WithTls().WithGatewayAnnotation("cert-manager.io/cluster-issuer", issuer.Resource.Name);
    }

    public static IResourceBuilder<KubernetesGatewayResource> WithGatewayAnnotation(this IResourceBuilder<KubernetesGatewayResource> builder, string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        builder.Resource.GatewayAnnotations[key] = value;
        return builder;
    }

    public static IResourceBuilder<KubernetesGatewayResource> WithGatewayAnnotation(this IResourceBuilder<KubernetesGatewayResource> builder, string key, IResourceBuilder<ParameterResource> value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        builder.Resource.GatewayAnnotations[key] = KubernetesValue.From(value.Resource);
        return builder;
    }
}

/// <summary>Configuration of <see cref="KubernetesHelmChartResource"/> (mirrors <c>KubernetesHelmChartExtensions</c>).</summary>
public static class PulumiKubernetesHelmChartExtensions
{
    public static IResourceBuilder<KubernetesHelmChartResource> WithHelmValue(this IResourceBuilder<KubernetesHelmChartResource> builder, string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        builder.Resource.Values[key] = value;
        return builder;
    }

    public static IResourceBuilder<KubernetesHelmChartResource> WithNamespace(this IResourceBuilder<KubernetesHelmChartResource> builder, string @namespace)
    {
        ArgumentException.ThrowIfNullOrEmpty(@namespace);
        builder.Resource.Namespace = @namespace;
        return builder;
    }

    public static IResourceBuilder<KubernetesHelmChartResource> WithReleaseName(this IResourceBuilder<KubernetesHelmChartResource> builder, string releaseName)
    {
        ArgumentException.ThrowIfNullOrEmpty(releaseName);
        builder.Resource.ReleaseName = releaseName;
        return builder;
    }

    /// <summary>Removes the release when the stack is destroyed (external charts are retained by default, as upstream).</summary>
    public static IResourceBuilder<KubernetesHelmChartResource> WithDestroy(this IResourceBuilder<KubernetesHelmChartResource> builder)
    {
        builder.Resource.DestroyOnUninstall = true;
        return builder;
    }

    /// <summary>Adopts resources that already exist in the cluster instead of failing (the closest Helm release option to <c>--force-conflicts</c>).</summary>
    public static IResourceBuilder<KubernetesHelmChartResource> WithForceConflicts(this IResourceBuilder<KubernetesHelmChartResource> builder)
    {
        builder.Resource.ForceConflicts = true;
        return builder;
    }
}

/// <summary>Configuration of cert-manager issuers (mirrors <c>CertManagerExtensions</c>).</summary>
public static class PulumiCertManagerExtensions
{
    private const string LetsEncryptProductionUrl = "https://acme-v02.api.letsencrypt.org/directory";
    private const string LetsEncryptStagingUrl = "https://acme-staging-v02.api.letsencrypt.org/directory";

    public static IResourceBuilder<CertManagerIssuerResource> AddIssuer(this IResourceBuilder<CertManagerResource> builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var issuer = new CertManagerIssuerResource(name, builder.Resource);
        builder.Resource.Issuers.Add(issuer);
        return builder.ApplicationBuilder.AddResource(issuer).WithParentRelationship(builder).WithIconName("ContactCard").ExcludeFromManifest();
    }

    public static IResourceBuilder<CertManagerIssuerResource> WithLetsEncryptProduction(this IResourceBuilder<CertManagerIssuerResource> builder, string email) =>
        builder.WithAcmeServer(LetsEncryptProductionUrl, email);

    public static IResourceBuilder<CertManagerIssuerResource> WithLetsEncryptProduction(this IResourceBuilder<CertManagerIssuerResource> builder, IResourceBuilder<ParameterResource> email) =>
        builder.WithAcmeServer(LetsEncryptProductionUrl, email);

    public static IResourceBuilder<CertManagerIssuerResource> WithLetsEncryptStaging(this IResourceBuilder<CertManagerIssuerResource> builder, string email) =>
        builder.WithAcmeServer(LetsEncryptStagingUrl, email);

    public static IResourceBuilder<CertManagerIssuerResource> WithLetsEncryptStaging(this IResourceBuilder<CertManagerIssuerResource> builder, IResourceBuilder<ParameterResource> email) =>
        builder.WithAcmeServer(LetsEncryptStagingUrl, email);

    public static IResourceBuilder<CertManagerIssuerResource> WithAcmeServer(this IResourceBuilder<CertManagerIssuerResource> builder, string serverUrl, string email)
    {
        ArgumentException.ThrowIfNullOrEmpty(serverUrl);
        ArgumentException.ThrowIfNullOrEmpty(email);
        builder.Resource.Spec = new CertManagerAcmeIssuerSpec(serverUrl, email);
        return builder;
    }

    public static IResourceBuilder<CertManagerIssuerResource> WithAcmeServer(this IResourceBuilder<CertManagerIssuerResource> builder, string serverUrl, IResourceBuilder<ParameterResource> email)
    {
        ArgumentException.ThrowIfNullOrEmpty(serverUrl);
        ArgumentNullException.ThrowIfNull(email);
        builder.Resource.Spec = new CertManagerAcmeIssuerSpec(serverUrl, KubernetesValue.From(email.Resource));
        return builder;
    }

    public static IResourceBuilder<CertManagerIssuerResource> WithHttp01Solver(this IResourceBuilder<CertManagerIssuerResource> builder)
    {
        builder.Resource.Http01Solver = true;
        return builder;
    }
}

/// <summary>Configuration and mounting of <see cref="KubernetesPersistentVolumeResource"/> (mirrors <c>KubernetesPersistentVolumeExtensions</c>).</summary>
public static class PulumiKubernetesPersistentVolumeExtensions
{
    public static IResourceBuilder<KubernetesPersistentVolumeResource> WithStorageClass(this IResourceBuilder<KubernetesPersistentVolumeResource> builder, string storageClassName)
    {
        ArgumentException.ThrowIfNullOrEmpty(storageClassName);
        builder.Resource.StorageClassName = storageClassName;
        return builder;
    }

    public static IResourceBuilder<KubernetesPersistentVolumeResource> WithStorageClass(this IResourceBuilder<KubernetesPersistentVolumeResource> builder, IResourceBuilder<ParameterResource> storageClassName)
    {
        ArgumentNullException.ThrowIfNull(storageClassName);
        builder.Resource.StorageClassName = KubernetesValue.From(storageClassName.Resource);
        return builder;
    }

    public static IResourceBuilder<KubernetesPersistentVolumeResource> WithCapacity(this IResourceBuilder<KubernetesPersistentVolumeResource> builder, string capacity)
    {
        ArgumentException.ThrowIfNullOrEmpty(capacity);
        builder.Resource.Capacity = capacity;
        return builder;
    }

    public static IResourceBuilder<KubernetesPersistentVolumeResource> WithCapacity(this IResourceBuilder<KubernetesPersistentVolumeResource> builder, IResourceBuilder<ParameterResource> capacity)
    {
        ArgumentNullException.ThrowIfNull(capacity);
        builder.Resource.Capacity = KubernetesValue.From(capacity.Resource);
        return builder;
    }

    public static IResourceBuilder<KubernetesPersistentVolumeResource> WithAccessMode(this IResourceBuilder<KubernetesPersistentVolumeResource> builder, PersistentVolumeAccessMode accessMode)
    {
        if (!builder.Resource.AccessModes.Contains(accessMode)) builder.Resource.AccessModes.Add(accessMode);
        return builder;
    }

    public static IResourceBuilder<KubernetesPersistentVolumeResource> WithVolumeAnnotation(this IResourceBuilder<KubernetesPersistentVolumeResource> builder, string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        builder.Resource.VolumeAnnotations[key] = value;
        return builder;
    }

    public static IResourceBuilder<KubernetesPersistentVolumeResource> WithVolumeAnnotation(this IResourceBuilder<KubernetesPersistentVolumeResource> builder, string key, IResourceBuilder<ParameterResource> value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        builder.Resource.VolumeAnnotations[key] = KubernetesValue.From(value.Resource);
        return builder;
    }

    /// <summary>Binds the claim to the resource's <c>WithVolume</c> mount of the same name.</summary>
    public static IResourceBuilder<T> WithPersistentVolume<T>(this IResourceBuilder<T> builder, IResourceBuilder<KubernetesPersistentVolumeResource> volume)
        where T : IComputeResource
    {
        ArgumentNullException.ThrowIfNull(volume);
        return builder.WithAnnotation(new KubernetesPersistentVolumeMountAnnotation(volume.Resource, null, false));
    }

    /// <summary>Mounts the claim at <paramref name="mountPath"/>.</summary>
    public static IResourceBuilder<T> WithPersistentVolume<T>(
        this IResourceBuilder<T> builder, IResourceBuilder<KubernetesPersistentVolumeResource> volume, string mountPath, bool isReadOnly = false)
        where T : IComputeResource
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentException.ThrowIfNullOrEmpty(mountPath);
        return builder.WithAnnotation(new KubernetesPersistentVolumeMountAnnotation(volume.Resource, mountPath, isReadOnly));
    }

    /// <summary>Mounts the claim at <paramref name="mountPath"/> and exposes the path in environment variable <paramref name="env"/>.</summary>
    public static IResourceBuilder<T> WithPersistentVolume<T>(
        this IResourceBuilder<T> builder, IResourceBuilder<KubernetesPersistentVolumeResource> volume, string mountPath, string env, bool isReadOnly = false)
        where T : IComputeResource, IResourceWithEnvironment
    {
        ArgumentException.ThrowIfNullOrEmpty(env);
        return builder.WithPersistentVolume(volume, mountPath, isReadOnly).WithEnvironment(env, mountPath);
    }
}
