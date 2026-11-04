# Sheridan.Aspire.Pulumi.Azure.Kubernetes

Azure Kubernetes Service (AKS) provisioned by Pulumi, with compute deployment through the Pulumi Kubernetes provider.

`AddPulumiKubernetes` provisions an AKS cluster and a container registry and acts as a compute environment: resources that target it are built, pushed and deployed as Kubernetes workloads. It ports `Aspire.Hosting.Azure.Kubernetes` and the deployment half of `Aspire.Hosting.Kubernetes`.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.Kubernetes
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var aks = builder.AddPulumiKubernetes("aks")
    .WithSystemNodePool("Standard_D4s_v5", 1, 5);

aks.AddNodePool("gpu", "Standard_NC4as_T4_v3", 0, 2);

var data = aks.AddPersistentVolume("data").WithCapacity("20Gi");

var api = builder.AddProject<Projects.MyApi>("api")
    .WithExternalHttpEndpoints()
    .WithPersistentVolume(data, "/data", env: "DATA_PATH");

aks.AddGateway("public")
    .WithHostname("example.com")
    .WithRoute("/", api.GetEndpoint("http"));

builder.Build().Run();
```

## Features

- Cluster: Free tier, autoscaled `system` pool and `workload` user pool, OIDC issuer and workload identity, plus a Basic registry (`{name}-acr`) with AcrPull for the kubelet identity.
- Per workload: `ServiceAccount`, `ConfigMap`, `Secret`, a `Deployment` (or `StatefulSet` with volumes) and a `ClusterIP` `Service`. The provider is configured from the cluster credentials, so no local kubeconfig is needed.
- Workload identity: one shared user-assigned identity with a federated credential per workload service account, so data integrations grant roles to AKS workloads unchanged.
- `AddIngress`, `AddGateway` (Gateway API), `AddLoadBalancer` (Application Gateway for Containers), `AddCertManager` / `AddIssuer`, `AddHelmChart` and `AddPersistentVolume`.
- Networking: `WithSubnet`, `WithNetworkProfile` and `AsPrivateCluster` (optionally with a Pulumi private DNS zone). Also `WithWorkloadIdentity` and `WithContainerInsights`.

## Notes

- Resource types keep the upstream names (`KubernetesIngressResource`, ...) but are Pulumi-side types: do not reference `Aspire.Hosting.Kubernetes` in the same AppHost.
- Everything deploys to the `default` namespace; bind mounts are ignored; Helm references must be OCI or URL based.
- Not ported: the Aspire dashboard component and `PublishAsKubernetesService` / Helm chart customization hooks (use `ConfigureInfrastructure` or a Helm release).
- Verified with `pulumi preview` only; a running AKS cluster has real ongoing cost.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.Kubernetes`](https://www.nuget.org/packages/Aspire.Hosting.Azure.Kubernetes) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/provisioning.md).
- Resource naming: [naming guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/naming.md).
