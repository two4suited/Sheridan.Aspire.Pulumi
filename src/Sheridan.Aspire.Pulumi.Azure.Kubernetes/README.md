# Sheridan.Aspire.Pulumi.Azure.Kubernetes

_Kubernetes (AKS)_ — part of [Sheridan.Aspire.Pulumi](../../README.md).

`Sheridan.Aspire.Pulumi.Azure.Kubernetes` ports `Aspire.Hosting.Azure.Kubernetes` (infrastructure) and the compute-deployment half of `Aspire.Hosting.Kubernetes`. `builder.AddPulumiKubernetes("aks")` (alias `AddPulumiAzureKubernetesEnvironment`) provisions an `azure-native:containerservice:ManagedCluster` matching the official Bicep: Free tier, system-assigned identity, an autoscaled `system` pool and a `workload` user pool (Standard_D2s_v5, 1-3 nodes), OIDC issuer and workload identity enabled, plus a Basic container registry (`{name}-acr`) whose `AcrPull` role is granted to the kubelet identity. Outputs (`ClusterFqdn`, `OidcIssuerUrl`, `KubeletIdentityObjectId`, `NodeResourceGroup`, `RegistryEndpoint`, ...) are exposed as `PulumiOutputReference` properties.

The environment is an `IComputeEnvironmentResource` (and `IContainerRegistry`, like the Container Apps environment): compute resources that target it (the only compute environment, or `WithComputeEnvironment(aks)`) are built, pushed to its registry and deployed with the Pulumi Kubernetes provider. The provider is configured from `listManagedClusterUserCredentials`, so a deploy needs only the stack, not a local kubeconfig. Per compute resource it emits a `ServiceAccount`, a `ConfigMap` (plain environment variables) and `Secret` (secret parameters), a `Deployment` (a `StatefulSet` when volumes are mounted, as upstream), pinned to the first user node pool, and a `ClusterIP` `Service` named `{name}-service` (what other workloads resolve through `GetHostAddressExpression`).

```csharp
var aks = builder.AddPulumiKubernetes("aks").WithPulumiStack(azureStack).WithResourceGroup(sharedRg)
    .WithSystemNodePool("Standard_D4s_v5", 1, 5)
    .WithContainerRegistry(registry)           // instead of the default '{name}-acr'
    .WithSubnet(nodesSubnet)                   // plain (non-delegated) subnet from the Network package
    .WithNetworkProfile(networkPolicy: "azure")   // typed options (upstream has no public setter)
    .AsPrivateCluster(privateDnsZone);         // or AsPrivateCluster() for the AKS-managed zone
aks.AddNodePool("gpu", "Standard_NC4as_T4_v3", 0, 2);   // user pool, optional .WithSubnet(...)

var data = aks.AddPersistentVolume("data").WithCapacity("20Gi");
var api = builder.AddProject<Projects.Api>("api").WithExternalHttpEndpoints()
    .WithPersistentVolume(data, "/data", env: "DATA_PATH");

var lb = aks.AddLoadBalancer("lb", albSubnet);          // Application Gateway for Containers
var cm = aks.AddCertManager("cert-manager");
var issuer = cm.AddIssuer("letsencrypt").WithLetsEncryptProduction("me@example.com").WithHttp01Solver();
aks.AddGateway("public").WithHostname("example.com").WithRoute("/", api.GetEndpoint("http")).WithTls(issuer).WithLoadBalancer(lb);
aks.AddIngress("legacy").WithIngressClass("nginx").WithPath("/api", api.GetEndpoint("http")).WithTls();
aks.AddHelmChart("metrics", "oci://registry-1.docker.io/bitnamicharts/metrics-server", "7.2.0").WithHelmValue("replicas", "2");
```

What each piece becomes (verified against the official publisher's output with a throwaway AppHost, and `pulumi preview` type-checks the generated program):

- **Workload identity (per-workload federated credentials).** Upstream creates one user-assigned identity per compute resource; here the environment owns one shared user-assigned identity (`{name}` + `Identity`, like the Container Apps environment's pull identity) and each workload's service account `{name}-sa` gets its own `FederatedIdentityCredential` on it (issuer = the cluster's OIDC issuer, subject `system:serviceaccount:default:{name}-sa`, audience `api://AzureADTokenExchange`; credentials are chained because Azure rejects concurrent writes to one identity). Because the environment implements `IPulumiManagedIdentityResource`, every integration that grants roles to a referencing app's compute environment (Storage, Service Bus, Key Vault, ...) works unchanged. The pods carry `azure.workload.identity/use`, so the workload-identity webhook injects `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/token file. The 20-credential-per-identity Azure limit applies to the shared identity.
- **Network profile / private cluster.** `WithNetworkProfile(plugin, policy, serviceCidr, dnsServiceIP)` (defaults Azure CNI, Calico, 10.0.4.0/22, 10.0.4.10) and `AsPrivateCluster()`. With a Pulumi private DNS zone, `AsPrivateCluster(zone)` switches the control plane to a user-assigned identity holding Private DNS Zone Contributor on the zone and Network Contributor on its VNet. As upstream, a node subnet implies `networkPlugin: azure` when no profile is set.
- **`AddIngress` / `AddGateway`** produce a `networking.k8s.io/v1` Ingress and a Gateway API `Gateway` + `HTTPRoute` (http listener on 80, https on 443 when TLS is set) with the upstream `With*` surface (class, hostnames, paths/routes, TLS, annotations, default backend). Routed endpoints must be external, as upstream enforces. Gateway API objects, ClusterIssuers and the ALB are applied as `kubernetes:yaml/v2:ConfigGroup` (Pulumi YAML does not support `CustomResource`).
- **`AddHelmChart`** is a `kubernetes:helm.sh/v3:Release` (OCI or URL chart references; `helm --set` style dotted keys are expanded and `true`/integers typed). External charts are retained on `pulumi destroy` unless `WithDestroy()` (upstream's `aspire destroy` behaviour); `WithForceConflicts()` maps to the release's `takeOwnership`, the closest equivalent of `--force-conflicts`. `AddCertManager` installs cert-manager (default `v1.20.2`, `crds.enabled`, Gateway API support) into the `cert-manager` namespace and `AddIssuer(...).WithLetsEncrypt*/WithAcmeServer/WithHttp01Solver` creates the `ClusterIssuer`s.
- **`AddLoadBalancer`** (Application Gateway for Containers) switches the cluster to the `2025-09-02-preview` AKS API (`azure-native:containerservice/v20250902preview:ManagedCluster`, required for `ingressProfile.gatewayAPI` / `applicationLoadBalancer`, as upstream), delegates the subnet to `Microsoft.ServiceNetworking/trafficControllers`, grants the ALB controller identity Network Contributor on it and applies the `ApplicationLoadBalancer` custom resource. A pipeline step (`aks-wait-alb-{name}`) polls for the `azure-alb-external` GatewayClass with `az`/`kubectl` before the deploy, like upstream's `apply-alb-crd` step; without `kubectl` it only warns and a second deploy may be needed.
- **`AddPersistentVolume`** is a `PersistentVolumeClaim` (capacity, storage class, access modes, annotations); `WithPersistentVolume(volume, path[, env])` mounts it, and a plain `WithVolume(...)` gets its own 1Gi claim.

Also `WithWorkloadIdentity(bool)` and `WithContainerInsights(logAnalyticsWorkspace)` (Log Analytics wiring via the `omsagent` add-on; upstream only exposes this as a property). The resource types keep the upstream names (`KubernetesIngressResource`, `CertManagerResource`, ...) but are Pulumi-side types in `Sheridan.Aspire.Pulumi.Azure.Kubernetes`, so do not reference `Aspire.Hosting.Kubernetes` in the same AppHost. Not deployed during development because a running AKS cluster (two nodes) has real ongoing cost.

Differences and limits: the Aspire dashboard component upstream adds to the cluster and `PublishAsKubernetesService`/Helm chart customization hooks are not ported (use `ConfigureInfrastructure` or a Helm release); bind mounts are ignored (warning); Helm chart references from `helm repo add` aliases are not supported (use OCI or URL references); everything is deployed to the `default` namespace; secret handling covers parameters marked secret (a connection string that embeds one is detected too) but secrets still land in Pulumi state, encrypted. Private clusters need the deploying machine to reach the cluster's VNet. `pulumi preview` was run against the generated programs (type-checked, nothing created); `pulumi up` against Azure was not, since it creates paid resources.
