# ContainerService overlay (issue #35)

Pulumi counterpart of `Aspire.Hosting.Azure.Provisioning.ContainerService`. Overlays `AddPulumiKubernetes`
(`AspirePulumiPackages.Pulumi.Azure.Kubernetes`). General mechanism: [../provisioning.md](../provisioning.md).

**Packaging decision:** the overlay lives alongside the hosting project (no sibling `...ContainerService.Provisioning` package, no opt-in gate).
The hook already existed (`PulumiKubernetesInfrastructure`) and is applied last in `ContributeResourcesAsync`.

## Roots

`ConfigureInfrastructure(Action<PulumiKubernetesInfrastructure>)` hands out:

| Upstream root | Property | azure-native resource |
| --- | --- | --- |
| `ContainerServiceManagedCluster` | `ContainerServiceManagedCluster` | `azure-native:containerservice:ManagedCluster` |
| (extra) | `ContainerRegistryService` | `azure-native:containerregistry:Registry` owned by the environment; throws if `WithContainerRegistry` supplied an external one |

Node pools and role assignments are reachable with `GetResource(id)` / `GetResources(type)`.

## Example

```csharp
builder.AddPulumiKubernetes("aks")
    .WithPulumiStack(stack)
    .ConfigureInfrastructure(infra =>
    {
        infra.ContainerServiceManagedCluster.Set("apiServerAccessProfile.enablePrivateCluster", true);
        infra.ContainerServiceManagedCluster.Set("kubernetesVersion", "1.31");
        infra.ContainerServiceManagedCluster.Set("disableLocalAccounts", true);
    });
```

Paths are `ManagedCluster` inputs per `pulumi package get-schema azure-native` (`apiServerAccessProfile`, `agentPoolProfiles`,
`networkProfile`, `oidcIssuerProfile`, `securityProfile`, `sku`, `kubernetesVersion`, ...). Verified with `aspire publish`:
the first example line emitted `apiServerAccessProfile: { enablePrivateCluster: true }` in `Pulumi.yaml`.

## Unsupported / excluded

- Compile-time types: upstream exposes `ContainerServiceManagedCluster` SDK models; we emit YAML, so properties are untyped dotted
  paths and typos surface at `pulumi up` (ADR 0001).
- Polyglot (ATS) projection: no Pulumi equivalent; C#-only.
- Experimental opt-in gate (`ASPIREAZUREPROVISIONING001`): not replicated.
- BicepValue operators/expressions: use `${...}` references and `PulumiExpr`.
