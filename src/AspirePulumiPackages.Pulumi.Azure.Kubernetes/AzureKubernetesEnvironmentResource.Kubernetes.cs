using System.Globalization;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using AspirePulumiPackages.Pulumi.Azure.Base;
using Microsoft.Extensions.Logging;

namespace AspirePulumiPackages.Pulumi.Azure.Kubernetes;

// Emission of the pulumi-kubernetes half of the environment: workload identity, the Kubernetes provider, compute resources, volumes,
// ingress, gateways, Helm releases, cert-manager and Application Gateway for Containers.
public sealed partial class AzureKubernetesEnvironmentResource
{
    /// <summary>The namespace everything is deployed to (upstream defaults to <c>default</c> too).</summary>
    internal const string KubernetesNamespace = "default";

    private const string WorkloadIdentityAudience = "api://AzureADTokenExchange";

    private string ProviderRef => $"{Id}K8s";

    private string K(string suffix) => Id + PulumiAzureProgramExtensions.ToPulumiId(suffix);

    private Dictionary<string, object?> Options(IEnumerable<string>? dependsOn = null, bool retainOnDelete = false)
    {
        var options = new Dictionary<string, object?> { ["provider"] = $"${{{ProviderRef}}}" };
        var deps = dependsOn?.Distinct().Select(d => (object?)$"${{{d}}}").ToList();
        if (deps is { Count: > 0 }) options["dependsOn"] = deps;
        if (retainOnDelete) options["retainOnDelete"] = true;
        return options;
    }

    private static Dictionary<string, object?> Res(string type, Dictionary<string, object?> properties, Dictionary<string, object?> options) =>
        new() { ["type"] = type, ["properties"] = properties, ["options"] = options };

    private static Dictionary<string, object?> Meta(string name, Dictionary<string, string>? labels = null, Dictionary<string, string>? annotations = null)
    {
        var meta = new Dictionary<string, object?> { ["name"] = name, ["namespace"] = KubernetesNamespace };
        if (labels is { Count: > 0 }) meta["labels"] = labels.ToDictionary(p => p.Key, p => (object?)p.Value);
        if (annotations is { Count: > 0 }) meta["annotations"] = annotations.ToDictionary(p => p.Key, p => (object?)p.Value);
        return meta;
    }

    /// <summary>Escapes a literal for a Pulumi YAML string ('${' would start an interpolation).</summary>
    private static string Lit(string value) => value.Replace("${", "$${");

    private async Task ContributeKubernetesAsync(
        PulumiProgram program, PipelineStepContext ctx, PulumiStackResource stack, string rg, string location, string registryRef,
        List<IResource> targeted, List<string> albJoins)
    {
        var any = targeted.Count > 0 || Ingresses.Count > 0 || Gateways.Count > 0 || HelmCharts.Count > 0 || PersistentVolumes.Count > 0 || LoadBalancers.Count > 0;
        if (!any) return;

        // The cluster has to exist before anything below can be applied, so the provision pass creates the cluster (and
        // other infrastructure) and the deploy pass applies everything emitted from here on: the workloads, in-cluster
        // objects and the Kubernetes provider are app resources (resources added with AddInfraResource stay infrastructure).
        using var appScope = program.BeginAppScope();
        program.HasApps = true;

        var useIdentity = WorkloadIdentityEnabled && targeted.Count > 0;
        var credentials = new Dictionary<string, string>(); // compute resource -> federated credential resource id

        if (useIdentity)
        {
            program.AddUserAssignedIdentity(IdentityRef, rg, location);
            program.Outputs[$"{IdentityRef}Id"] = $"${{{IdentityRef}.id}}";
            program.Outputs[$"{IdentityRef}ClientId"] = $"${{{IdentityRef}.clientId}}";
            program.Outputs[$"{IdentityRef}PrincipalId"] = $"${{{IdentityRef}.principalId}}";

            // One federated credential per workload service account. Azure rejects concurrent writes to one identity's credentials, so chain them.
            string? previous = null;
            foreach (var r in targeted)
            {
                var credId = K(r.Name + "Fedcred");
                var options = new Dictionary<string, object?>();
                if (previous is not null) options["dependsOn"] = new List<object?> { $"${{{previous}}}" };
                program.Resources[credId] = new Dictionary<string, object?>
                {
                    ["type"] = "azure-native:managedidentity:FederatedIdentityCredential",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["resourceGroupName"] = rg,
                        ["resourceName"] = $"${{{IdentityRef}.name}}",
                        ["federatedIdentityCredentialResourceName"] = $"{r.Name}-fedcred",
                        ["issuer"] = $"${{{Id}.oidcIssuerProfile.issuerURL}}",
                        ["subject"] = $"system:serviceaccount:{KubernetesNamespace}:{ToKubernetesName(r.Name)}-sa",
                        ["audiences"] = new List<object?> { WorkloadIdentityAudience },
                    },
                    ["options"] = options,
                };
                credentials[r.Name] = credId;
                previous = credId;
            }
        }

        // Kubernetes provider: the cluster's user credentials (listClusterUser), decoded.
        program.Variables[$"{Id}Credentials"] = new Dictionary<string, object?>
        {
            ["fn::invoke"] = new Dictionary<string, object?>
            {
                ["function"] = "azure-native:containerservice:listManagedClusterUserCredentials",
                ["arguments"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["resourceName"] = $"${{{Id}.name}}",
                },
            },
        };
        program.Variables[$"{Id}Kubeconfig"] = new Dictionary<string, object?>
        {
            ["fn::secret"] = new Dictionary<string, object?>
            {
                ["fn::fromBase64"] = $"${{{Id}Credentials.kubeconfigs[0].value}}",
            },
        };
        program.Resources[ProviderRef] = new Dictionary<string, object?>
        {
            ["type"] = "pulumi:providers:kubernetes",
            ["properties"] = new Dictionary<string, object?> { ["kubeconfig"] = $"${{{Id}Kubeconfig}}" },
        };

        // Everything below needs the cluster, its registry pull access and (for AGC) the controller's subnet permissions.
        var clusterReady = new List<string> { Id, $"{Id}AcrPull" };
        clusterReady.AddRange(albJoins);

        var volumeIds = new Dictionary<string, string>(); // claim name -> pulumi id
        foreach (var pv in PersistentVolumes)
            volumeIds[pv.ClaimName] = await AddPersistentVolumeClaimAsync(program, pv, clusterReady, ctx.CancellationToken);

        var serviceIds = new Dictionary<string, string>(); // compute resource name -> Service pulumi id
        foreach (var r in targeted)
        {
            var serviceId = await AddWorkloadAsync(program, ctx, stack, r, registryRef, useIdentity, credentials, volumeIds, clusterReady);
            serviceIds[r.Name] = serviceId;
        }

        // Helm releases (cert-manager's chart is one of them).
        var releaseIds = new Dictionary<KubernetesHelmChartResource, string>();
        foreach (var chart in HelmCharts)
            releaseIds[chart] = AddHelmRelease(program, chart, clusterReady);

        // Application Gateway for Containers load balancers: the ApplicationLoadBalancer custom resource.
        var lbIds = new Dictionary<AzureKubernetesLoadBalancerResource, string>();
        foreach (var lb in LoadBalancers)
        {
            var id = K(lb.Name + "Alb");
            program.Resources[id] = ConfigGroup(new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["apiVersion"] = "alb.networking.azure.io/v1",
                    ["kind"] = "ApplicationLoadBalancer",
                    ["metadata"] = new Dictionary<string, object?>
                    {
                        ["name"] = lb.AlbName,
                        ["namespace"] = AzureKubernetesLoadBalancerResource.AlbNamespace,
                    },
                    ["spec"] = new Dictionary<string, object?>
                    {
                        ["associations"] = new List<object?> { lb.Subnet.IdExpression },
                    },
                },
            }, Options(clusterReady));
            lbIds[lb] = id;
        }

        // cert-manager ClusterIssuers (after the chart has installed the CRDs).
        var issuerIds = new Dictionary<string, string>();
        foreach (var cm in CertManagers)
        {
            foreach (var issuer in cm.Issuers)
            {
                var id = K(issuer.Name + "ClusterIssuer");
                program.Resources[id] = ConfigGroup(
                    [await BuildClusterIssuerAsync(issuer, ctx)],
                    Options([releaseIds[cm.HelmChart]]));
                issuerIds[issuer.Name] = id;
            }
        }

        foreach (var ingress in Ingresses.Where(i => i.ShouldMaterialize))
            await AddIngressAsync(program, ingress, targeted, serviceIds, lbIds, clusterReady, ctx.CancellationToken);

        foreach (var gateway in Gateways.Where(g => g.ShouldMaterialize))
            await AddGatewayAsync(program, gateway, targeted, serviceIds, lbIds, issuerIds, clusterReady, ctx.CancellationToken);
    }

    private static Dictionary<string, object?> ConfigGroup(List<object?> objs, Dictionary<string, object?> options) => new()
    {
        ["type"] = "kubernetes:yaml/v2:ConfigGroup",
        ["properties"] = new Dictionary<string, object?> { ["objs"] = objs },
        ["options"] = options,
    };

    // ---------------------------------------------------------------- persistent volumes

    private async Task<string> AddPersistentVolumeClaimAsync(PulumiProgram program, KubernetesPersistentVolumeResource pv, List<string> deps, CancellationToken ct)
    {
        var id = K(pv.Name + "Claim");
        var spec = new Dictionary<string, object?>
        {
            ["accessModes"] = (pv.AccessModes.Count > 0 ? pv.AccessModes : [PersistentVolumeAccessMode.ReadWriteOnce]).Select(m => (object?)m.ToString()).ToList(),
            ["resources"] = new Dictionary<string, object?>
            {
                ["requests"] = new Dictionary<string, object?> { ["storage"] = Lit(pv.Capacity is { } c ? await c.ResolveAsync(ct) : "1Gi") },
            },
        };
        if (pv.StorageClassName is { } sc) spec["storageClassName"] = Lit(await sc.ResolveAsync(ct));

        var annotations = new Dictionary<string, string> { ["pulumi.com/skipAwait"] = "true" };
        foreach (var (key, value) in pv.VolumeAnnotations) annotations[key] = Lit(await value.ResolveAsync(ct));

        program.Resources[id] = Res("kubernetes:core/v1:PersistentVolumeClaim",
            new() { ["metadata"] = Meta(pv.ClaimName, annotations: annotations), ["spec"] = spec }, Options(deps));
        return id;
    }

    // ---------------------------------------------------------------- compute resources

    private static bool IsSecret(object? value) => value switch
    {
        ParameterResource p => p.Secret,
        ReferenceExpression e => e.ValueProviders.Any(v => IsSecret(v)),
        _ => false,
    };

    private async Task<string> AddWorkloadAsync(
        PulumiProgram program, PipelineStepContext ctx, PulumiStackResource stack, IResource r, string registryRef, bool useIdentity,
        Dictionary<string, string> credentials, Dictionary<string, string> volumeIds, List<string> clusterReady)
    {
        var app = ToKubernetesName(r.Name);
        var imageName = r.Name.ToLowerInvariant();
        var labels = new Dictionary<string, string>
        {
            ["app.kubernetes.io/name"] = ToKubernetesName(Name),
            ["app.kubernetes.io/component"] = app,
            ["app.kubernetes.io/managed-by"] = "aspire",
        };
        var selector = new Dictionary<string, object?>
        {
            ["app.kubernetes.io/name"] = labels["app.kubernetes.io/name"],
            ["app.kubernetes.io/component"] = app,
        };

        var endpoints = r.TryGetEndpoints(out var eps) ? eps.ToList() : [];
        var httpPort = endpoints.Where(e => e.UriScheme is "http" or "https").Select(ContainerPort).Cast<int?>().FirstOrDefault() ?? 8080;

        // Environment: plain values go to a ConfigMap, secret parameters to a Secret.
        var envVars = new Dictionary<string, object>();
        var envContext = new EnvironmentCallbackContext(ctx.ExecutionContext, r, envVars, ctx.CancellationToken);
        foreach (var a in r.Annotations.OfType<EnvironmentCallbackAnnotation>())
            await a.Callback(envContext);

        var config = new Dictionary<string, object?>();
        var secrets = new Dictionary<string, object?>();
        foreach (var (key, value) in envVars)
        {
            // Endpoint-reference ports can't be resolved at publish time; the container listens on the endpoint's target port.
            if (key is "HTTP_PORTS")
            {
                config[key] = httpPort.ToString(CultureInfo.InvariantCulture);
                continue;
            }

            string? resolved;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ctx.CancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                resolved = value switch
                {
                    string s => s,
                    IValueProvider p => await p.GetValueAsync(timeout.Token),
                    _ => value.ToString(),
                };
            }
            catch (Exception e) when (e is not OperationCanceledException || !ctx.CancellationToken.IsCancellationRequested)
            {
                // Outputs of infrastructure that doesn't exist yet resolve after the provision pass, when deploy regenerates the program.
                ctx.Logger.LogDeferredEnvironmentVariable(stack, key, r.Name, e);
                continue;
            }

            if (resolved is null) continue;
            (IsSecret(value) ? secrets : config)[key] = Lit(resolved);
        }

        var dependsOn = new List<string>(clusterReady);

        var envFrom = new List<object?>();
        if (config.Count > 0)
        {
            var id = K(r.Name + "Config");
            program.Resources[id] = Res("kubernetes:core/v1:ConfigMap",
                new() { ["metadata"] = Meta($"{app}-config", labels), ["data"] = config }, Options(clusterReady));
            envFrom.Add(new Dictionary<string, object?> { ["configMapRef"] = new Dictionary<string, object?> { ["name"] = $"{app}-config" } });
            dependsOn.Add(id);
        }
        if (secrets.Count > 0)
        {
            var id = K(r.Name + "Secrets");
            program.Resources[id] = Res("kubernetes:core/v1:Secret",
                new() { ["metadata"] = Meta($"{app}-secrets", labels), ["stringData"] = secrets }, Options(clusterReady));
            envFrom.Add(new Dictionary<string, object?> { ["secretRef"] = new Dictionary<string, object?> { ["name"] = $"{app}-secrets" } });
            dependsOn.Add(id);
        }

        // Workload identity: a service account federated to the environment's shared identity (upstream: '{name}-sa' + azure.workload.identity/*).
        var podLabels = new Dictionary<string, string>(labels);
        var serviceAccount = (string?)null;
        if (useIdentity)
        {
            serviceAccount = $"{app}-sa";
            var id = K(r.Name + "ServiceAccount");
            program.Resources[id] = Res("kubernetes:core/v1:ServiceAccount",
                new()
                {
                    ["metadata"] = Meta(serviceAccount, new Dictionary<string, string>(labels) { ["azure.workload.identity/use"] = "true" },
                        new Dictionary<string, string> { ["azure.workload.identity/client-id"] = $"${{{IdentityRef}.clientId}}" }),
                },
                Options(clusterReady));
            podLabels["azure.workload.identity/use"] = "true";
            dependsOn.Add(id);
            dependsOn.Add(credentials[r.Name]);
        }

        // Volumes: persistent volume claims bound by WithPersistentVolume, otherwise a claim per named volume.
        var volumes = new List<object?>();
        var volumeMounts = new List<object?>();
        var containerMounts = r.Annotations.OfType<ContainerMountAnnotation>().ToList();
        var pvMounts = r.Annotations.OfType<KubernetesPersistentVolumeMountAnnotation>().ToList();
        var boundSources = new HashSet<string>();
        var volumeCount = 0;

        void Mount(string claim, string claimId, string path, bool readOnly)
        {
            var volumeName = $"vol{volumeCount++}";
            volumes.Add(new Dictionary<string, object?>
            {
                ["name"] = volumeName,
                ["persistentVolumeClaim"] = new Dictionary<string, object?> { ["claimName"] = claim },
            });
            volumeMounts.Add(new Dictionary<string, object?> { ["name"] = volumeName, ["mountPath"] = path, ["readOnly"] = readOnly });
            dependsOn.Add(claimId);
        }

        foreach (var m in pvMounts)
        {
            if (!volumeIds.TryGetValue(m.Volume.ClaimName, out var claimId))
                throw new InvalidOperationException($"'{r.Name}' mounts persistent volume '{m.Volume.Name}', which belongs to a different AKS environment.");
            var mount = m.MountPath is not null
                ? (Path: m.MountPath, m.IsReadOnly)
                : containerMounts.Where(c => c.Type == ContainerMountType.Volume && c.Source == m.Volume.Name).Select(c => (Path: c.Target, c.IsReadOnly)).FirstOrDefault();
            if (mount.Path is null)
                throw new InvalidOperationException(
                    $"'{r.Name}' binds persistent volume '{m.Volume.Name}' without a mount path, but has no WithVolume(\"{m.Volume.Name}\", <path>) mount to take it from.");
            boundSources.Add(m.Volume.Name);
            Mount(m.Volume.ClaimName, claimId, mount.Path, mount.IsReadOnly);
        }

        foreach (var c in containerMounts)
        {
            if (c.Type != ContainerMountType.Volume)
            {
                ctx.Logger.LogWarning("Bind mount '{Source}' of {Resource} is not supported on Kubernetes and is ignored.", c.Source, r.Name);
                continue;
            }
            if (c.Source is not null && boundSources.Contains(c.Source)) continue;

            var claim = $"{app}-{ToKubernetesName(c.Source ?? $"volume{volumeCount}")}";
            var claimId = K(claim + "Claim");
            program.Resources[claimId] = Res("kubernetes:core/v1:PersistentVolumeClaim",
                new()
                {
                    ["metadata"] = Meta(claim, labels, new Dictionary<string, string> { ["pulumi.com/skipAwait"] = "true" }),
                    ["spec"] = new Dictionary<string, object?>
                    {
                        ["accessModes"] = new List<object?> { "ReadWriteOnce" },
                        ["resources"] = new Dictionary<string, object?> { ["requests"] = new Dictionary<string, object?> { ["storage"] = "1Gi" } },
                    },
                }, Options(clusterReady));
            Mount(claim, claimId, c.Target, c.IsReadOnly);
        }

        var container = new Dictionary<string, object?>
        {
            ["name"] = app,
            ["image"] = $"${{{registryRef}.loginServer}}/{imageName}:{stack.DeployTag}",
        };
        if (envFrom.Count > 0) container["envFrom"] = envFrom;
        if (endpoints.Count > 0)
        {
            container["ports"] = endpoints.GroupBy(ContainerPort).Select(g => (object?)new Dictionary<string, object?>
            {
                ["name"] = PortName(g.First(), g.Key),
                ["containerPort"] = g.Key,
                ["protocol"] = ProtocolOf(g.First()),
            }).ToList();
        }
        if (volumeMounts.Count > 0) container["volumeMounts"] = volumeMounts;

        var podSpec = new Dictionary<string, object?>
        {
            ["containers"] = new List<object?> { container },
            // Upstream pins workloads to the first user node pool.
            ["nodeSelector"] = new Dictionary<string, object?> { ["agentpool"] = EffectivePools().First(p => p.Config.Mode == AksNodePoolMode.User).Config.Name },
        };
        if (serviceAccount is not null) podSpec["serviceAccountName"] = serviceAccount;
        if (volumes.Count > 0)
        {
            podSpec["volumes"] = volumes;
            podSpec["securityContext"] = new Dictionary<string, object?> { ["fsGroup"] = 2000, ["fsGroupChangePolicy"] = "OnRootMismatch" };
        }

        var replicas = r.TryGetLastAnnotation<ReplicaAnnotation>(out var replicaAnnotation) ? replicaAnnotation.Replicas : 1;
        var stateful = volumes.Count > 0;
        var serviceName = ServiceName(r.Name);
        var spec = new Dictionary<string, object?>
        {
            ["replicas"] = replicas,
            ["selector"] = new Dictionary<string, object?> { ["matchLabels"] = selector },
            ["template"] = new Dictionary<string, object?>
            {
                ["metadata"] = new Dictionary<string, object?> { ["labels"] = podLabels.ToDictionary(p => p.Key, p => (object?)p.Value) },
                ["spec"] = podSpec,
            },
        };
        if (stateful) spec["serviceName"] = serviceName;

        var workloadId = K(r.Name + (stateful ? "StatefulSet" : "Deployment"));
        program.Resources[workloadId] = Res(
            stateful ? "kubernetes:apps/v1:StatefulSet" : "kubernetes:apps/v1:Deployment",
            new() { ["metadata"] = Meta($"{app}-{(stateful ? "statefulset" : "deployment")}", labels), ["spec"] = spec },
            Options(dependsOn));

        // Service: one port per distinct container port, ClusterIP (ingress/gateway expose it).
        var serviceId = K(r.Name + "Service");
        var ports = endpoints.GroupBy(ContainerPort).Select(g => (object?)new Dictionary<string, object?>
        {
            ["name"] = PortName(g.First(), g.Key),
            ["port"] = g.Key,
            ["targetPort"] = g.Key,
            ["protocol"] = ProtocolOf(g.First()),
        }).ToList();
        // A Service without ports is not valid, so workloads without endpoints get none.
        if (ports.Count == 0) return workloadId;
        program.Resources[serviceId] = Res("kubernetes:core/v1:Service",
            new()
            {
                ["metadata"] = Meta(serviceName, labels),
                ["spec"] = new Dictionary<string, object?>
                {
                    ["type"] = "ClusterIP",
                    ["selector"] = selector,
                    ["ports"] = ports,
                },
            },
            Options(clusterReady));
        return serviceId;
    }

    private static string PortName(EndpointAnnotation e, int port)
    {
        var name = e.Name.ToLowerInvariant();
        return name.Length is > 0 and <= 15 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') && char.IsAsciiLetter(name.Replace("-", "").FirstOrDefault())
            ? name
            : $"port-{port}";
    }

    private static string ProtocolOf(EndpointAnnotation e) => e.Protocol == System.Net.Sockets.ProtocolType.Udp ? "UDP" : "TCP";

    // ---------------------------------------------------------------- helm

    private string AddHelmRelease(PulumiProgram program, KubernetesHelmChartResource chart, List<string> clusterReady)
    {
        var id = K(chart.Name + "Release");
        var ns = chart.Namespace;
        var properties = new Dictionary<string, object?>
        {
            ["name"] = chart.ReleaseName ?? ToKubernetesName(chart.Name),
            ["chart"] = chart.ChartReference,
            ["version"] = chart.ChartVersion,
        };
        if (ns is not null)
        {
            properties["namespace"] = ns;
            properties["createNamespace"] = true;
        }
        if (chart.Values.Count > 0) properties["values"] = HelmValues(chart.Values);
        if (chart.ForceConflicts) properties["takeOwnership"] = true;

        program.Resources[id] = Res("kubernetes:helm.sh/v3:Release", properties, Options(clusterReady, retainOnDelete: !chart.DestroyOnUninstall));
        return id;
    }

    /// <summary>Expands <c>helm --set</c> style dotted keys into nested values; <c>true</c>/<c>false</c>/integers are typed as helm does.</summary>
    internal static Dictionary<string, object?> HelmValues(IReadOnlyDictionary<string, string> flat)
    {
        var root = new Dictionary<string, object?>();
        foreach (var (key, raw) in flat)
        {
            var parts = key.Split('.');
            var node = root;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (!node.TryGetValue(parts[i], out var next) || next is not Dictionary<string, object?> child)
                    node[parts[i]] = child = new Dictionary<string, object?>();
                node = child;
            }
            node[parts[^1]] = bool.TryParse(raw, out var b) ? b
                : long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var l) && (raw == "0" || !raw.StartsWith('0')) ? l
                : Lit(raw);
        }
        return root;
    }

    // ---------------------------------------------------------------- cert-manager

    private async Task<object?> BuildClusterIssuerAsync(CertManagerIssuerResource issuer, PipelineStepContext ctx)
    {
        if (issuer.Spec is not { } acme)
            throw new InvalidOperationException($"ClusterIssuer '{issuer.Name}' has no spec. Configure it with WithLetsEncryptProduction(), WithLetsEncryptStaging() or WithAcmeServer().");
        if (!issuer.Http01Solver)
            throw new InvalidOperationException($"ClusterIssuer '{issuer.Name}' has no solvers configured. Add at least one solver via WithHttp01Solver().");

        var name = ToKubernetesName(issuer.Name);
        var gateways = Gateways
            .Where(g => g.ShouldMaterialize && g.GatewayAnnotations.TryGetValue("cert-manager.io/cluster-issuer", out var v) && v.Literal == issuer.Name)
            .ToList();
        if (gateways.Count == 0)
        {
            ctx.Logger.LogWarning(
                "ClusterIssuer '{Issuer}' has an HTTP-01 solver but no Gateway in '{Environment}' uses it and has a route; cert-manager cannot satisfy ACME challenges until one does (WithRoute(...) and WithTls(issuer)).",
                issuer.Name, Name);
        }

        return new Dictionary<string, object?>
        {
            ["apiVersion"] = "cert-manager.io/v1",
            ["kind"] = "ClusterIssuer",
            ["metadata"] = new Dictionary<string, object?> { ["name"] = name },
            ["spec"] = new Dictionary<string, object?>
            {
                ["acme"] = new Dictionary<string, object?>
                {
                    ["server"] = Lit(await acme.ServerUrl.ResolveAsync(ctx.CancellationToken)),
                    ["email"] = Lit(await acme.Email.ResolveAsync(ctx.CancellationToken)),
                    ["privateKeySecretRef"] = new Dictionary<string, object?> { ["name"] = $"{name}-account-key" },
                    ["solvers"] = gateways.Count == 0
                        ? new List<object?>()
                        : new List<object?>
                        {
                            new Dictionary<string, object?>
                            {
                                ["http01"] = new Dictionary<string, object?>
                                {
                                    ["gatewayHTTPRoute"] = new Dictionary<string, object?>
                                    {
                                        ["parentRefs"] = gateways.Select(g => (object?)new Dictionary<string, object?>
                                        {
                                            ["group"] = "gateway.networking.k8s.io",
                                            ["kind"] = "Gateway",
                                            ["name"] = ToKubernetesName(g.Name),
                                        }).ToList(),
                                    },
                                },
                            },
                        },
                },
            },
        };
    }

    // ---------------------------------------------------------------- ingress / gateway

    /// <summary>The Service name and port of a routed endpoint, validated as upstream does.</summary>
    private static (string ServiceId, string Service, int Port) Backend(
        EndpointReference endpoint, string routedBy, List<IResource> targeted, Dictionary<string, string> serviceIds)
    {
        var resource = endpoint.Resource.Name;
        if (!endpoint.EndpointAnnotation.IsExternal)
            throw new InvalidOperationException(
                $"Resource '{resource}' endpoint '{endpoint.EndpointName}' is not marked as external but is being routed by '{routedBy}'. " +
                "Call .WithExternalHttpEndpoints() on the target resource or pass isExternal: true when creating the endpoint.");
        if (!targeted.Any(t => t.Name == resource) || !serviceIds.TryGetValue(resource, out var serviceId))
            throw new InvalidOperationException($"'{routedBy}' routes to '{resource}', which is not deployed to this AKS environment.");
        return (serviceId, ServiceName(resource), ContainerPort(endpoint.EndpointAnnotation));
    }

    private static async Task<Dictionary<string, string>> ResolveAnnotationsAsync(Dictionary<string, KubernetesValue> source, CancellationToken ct)
    {
        var result = new Dictionary<string, string>();
        foreach (var (key, value) in source) result[key] = Lit(await value.ResolveAsync(ct));
        return result;
    }

    private async Task AddIngressAsync(
        PulumiProgram program, KubernetesIngressResource ingress, List<IResource> targeted, Dictionary<string, string> serviceIds,
        Dictionary<AzureKubernetesLoadBalancerResource, string> lbIds, List<string> clusterReady, CancellationToken ct)
    {
        var dependsOn = new List<string>(clusterReady);
        var hostnames = new List<string>();
        foreach (var h in ingress.Hostnames) hostnames.Add(Lit(await h.ResolveAsync(ct)));

        // Group paths by host; a path without a host applies to every hostname (or to all hosts when none was set).
        var byHost = new Dictionary<string, List<object?>>();
        const string anyHost = "";
        foreach (var p in ingress.Paths)
        {
            var (serviceId, service, port) = Backend(p.Endpoint, ingress.Name, targeted, serviceIds);
            dependsOn.Add(serviceId);
            var entry = new Dictionary<string, object?>
            {
                ["path"] = p.Path,
                ["pathType"] = p.PathType.ToString(),
                ["backend"] = ServiceBackend(service, port),
            };
            var hosts = p.Host is not null ? [p.Host] : hostnames.Count > 0 ? hostnames : new List<string> { anyHost };
            foreach (var host in hosts)
            {
                if (!byHost.TryGetValue(host, out var list)) byHost[host] = list = [];
                list.Add(entry);
            }
        }

        var spec = new Dictionary<string, object?>();
        if (ingress.IngressClassName is { } cls) spec["ingressClassName"] = Lit(await cls.ResolveAsync(ct));
        if (byHost.Count > 0)
        {
            spec["rules"] = byHost.Select(h =>
            {
                var rule = new Dictionary<string, object?> { ["http"] = new Dictionary<string, object?> { ["paths"] = h.Value } };
                if (h.Key != anyHost) rule["host"] = h.Key;
                return (object?)rule;
            }).ToList();
        }
        if (ingress.DefaultBackend is { } db)
        {
            var (serviceId, service, port) = Backend(db.Endpoint, ingress.Name, targeted, serviceIds);
            dependsOn.Add(serviceId);
            spec["defaultBackend"] = ServiceBackend(service, port);
        }
        if (ingress.TlsConfigs.Count > 0)
        {
            var tls = new List<object?>();
            foreach (var t in ingress.TlsConfigs)
            {
                var entry = new Dictionary<string, object?> { ["secretName"] = Lit(await t.SecretName.ResolveAsync(ct)) };
                if (hostnames.Count > 0) entry["hosts"] = hostnames.Cast<object?>().ToList();
                tls.Add(entry);
            }
            spec["tls"] = tls;
        }

        var annotations = await ResolveAnnotationsAsync(ingress.IngressAnnotations, ct);
        dependsOn.AddRange(LoadBalancerDependencies(annotations, lbIds));
        program.Resources[K(ingress.Name + "Ingress")] = Res("kubernetes:networking.k8s.io/v1:Ingress",
            new() { ["metadata"] = Meta(ToKubernetesName(ingress.Name), annotations: annotations), ["spec"] = spec }, Options(dependsOn));
    }

    private static Dictionary<string, object?> ServiceBackend(string service, int port) => new()
    {
        ["service"] = new Dictionary<string, object?>
        {
            ["name"] = service,
            ["port"] = new Dictionary<string, object?> { ["number"] = port },
        },
    };

    private static IEnumerable<string> LoadBalancerDependencies(Dictionary<string, string> annotations, Dictionary<AzureKubernetesLoadBalancerResource, string> lbIds) =>
        annotations.TryGetValue("alb.networking.azure.io/alb-name", out var albName)
            ? lbIds.Where(l => l.Key.AlbName == albName).Select(l => l.Value)
            : [];

    private async Task AddGatewayAsync(
        PulumiProgram program, KubernetesGatewayResource gateway, List<IResource> targeted, Dictionary<string, string> serviceIds,
        Dictionary<AzureKubernetesLoadBalancerResource, string> lbIds, Dictionary<string, string> issuerIds,
        List<string> clusterReady, CancellationToken ct)
    {
        if (gateway.GatewayClassName is not { } gatewayClass)
            throw new InvalidOperationException($"Gateway '{gateway.Name}' has no gateway class. Call WithGatewayClass(...) or WithLoadBalancer(...).");

        var name = ToKubernetesName(gateway.Name);
        var dependsOn = new List<string>(clusterReady);
        var hostnames = new List<string>();
        foreach (var h in gateway.Hostnames) hostnames.Add(Lit(await h.ResolveAsync(ct)));
        var annotations = await ResolveAnnotationsAsync(gateway.GatewayAnnotations, ct);
        dependsOn.AddRange(LoadBalancerDependencies(annotations, lbIds));
        if (annotations.TryGetValue("cert-manager.io/cluster-issuer", out var issuer) && issuerIds.TryGetValue(issuer, out var issuerId))
            dependsOn.Add(issuerId);

        var listeners = new List<object?>
        {
            new Dictionary<string, object?>
            {
                ["name"] = "http",
                ["protocol"] = "HTTP",
                ["port"] = 80,
                ["allowedRoutes"] = new Dictionary<string, object?> { ["namespaces"] = new Dictionary<string, object?> { ["from"] = "Same" } },
            },
        };
        if (gateway.TlsConfigs.Count > 0)
        {
            var refs = new List<object?>();
            foreach (var t in gateway.TlsConfigs)
                refs.Add(new Dictionary<string, object?> { ["name"] = Lit(await t.SecretName.ResolveAsync(ct)) });
            var https = new Dictionary<string, object?>
            {
                ["name"] = "https",
                ["protocol"] = "HTTPS",
                ["port"] = 443,
                ["tls"] = new Dictionary<string, object?> { ["mode"] = "Terminate", ["certificateRefs"] = refs },
                ["allowedRoutes"] = new Dictionary<string, object?> { ["namespaces"] = new Dictionary<string, object?> { ["from"] = "Same" } },
            };
            if (hostnames.Count == 1) https["hostname"] = hostnames[0];
            listeners.Add(https);
        }

        var objs = new List<object?>
        {
            new Dictionary<string, object?>
            {
                ["apiVersion"] = "gateway.networking.k8s.io/v1",
                ["kind"] = "Gateway",
                ["metadata"] = Meta(name, annotations: annotations),
                ["spec"] = new Dictionary<string, object?>
                {
                    ["gatewayClassName"] = Lit(await gatewayClass.ResolveAsync(ct)),
                    ["listeners"] = listeners,
                },
            },
        };

        // One HTTPRoute per host group; routes without a host use the gateway's hostnames.
        var groups = new Dictionary<string, (List<string> Hosts, List<object?> Rules)>();
        foreach (var route in gateway.Routes)
        {
            var (serviceId, service, port) = Backend(route.Endpoint, gateway.Name, targeted, serviceIds);
            dependsOn.Add(serviceId);
            var hosts = route.Host is not null ? [route.Host] : hostnames;
            var key = string.Join(',', hosts);
            if (!groups.TryGetValue(key, out var group)) groups[key] = group = (hosts, []);
            group.Rules.Add(new Dictionary<string, object?>
            {
                ["matches"] = new List<object?>
                {
                    new Dictionary<string, object?>
                    {
                        ["path"] = new Dictionary<string, object?> { ["type"] = route.PathType.ToString(), ["value"] = route.Path },
                    },
                },
                ["backendRefs"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["name"] = service, ["port"] = port },
                },
            });
        }

        var index = 0;
        foreach (var (hosts, rules) in groups.Values)
        {
            var routeSpec = new Dictionary<string, object?>
            {
                ["parentRefs"] = new List<object?> { new Dictionary<string, object?> { ["name"] = name } },
                ["rules"] = rules,
            };
            if (hosts.Count > 0) routeSpec["hostnames"] = hosts.Cast<object?>().ToList();
            objs.Add(new Dictionary<string, object?>
            {
                ["apiVersion"] = "gateway.networking.k8s.io/v1",
                ["kind"] = "HTTPRoute",
                ["metadata"] = Meta(index == 0 ? $"{name}-route" : $"{name}-route-{index}"),
                ["spec"] = routeSpec,
            });
            index++;
        }

        program.Resources[K(gateway.Name + "Gateway")] = ConfigGroup(objs, Options(dependsOn));
    }
}
