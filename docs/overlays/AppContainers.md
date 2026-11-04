# AppContainers overlay (issue #29)

Pulumi counterpart of upstream `Aspire.Hosting.Azure.Provisioning.AppContainers`, which overlays the Container Apps
environment integration. General mechanics are in [../provisioning.md](../provisioning.md).

**Packaging:** the overlay lives alongside the hosting project (`Sheridan.Aspire.Pulumi.Azure.AppContainers`,
`PulumiContainerAppsInfrastructure.cs`). There is no sibling opt-in `...Provisioning` package.

## Roots

`builder.AddPulumiAzureContainerAppEnvironment(...).ConfigureInfrastructure(infra => ...)` hands you a `PulumiContainerAppsInfrastructure`:

| Root (upstream name) | Pulumi resource |
| --- | --- |
| `ContainerAppManagedEnvironment` | `azure-native:app:ManagedEnvironment` |
| `GetContainerApp("name")` (apps/jobs via identifier lookup) | `azure-native:app:ContainerApp` or `azure-native:app:Job` for the compute resource `name` |

Other resources (Log Analytics workspace `<id>Logs`, registry, pull identity, dashboard `<id>Dashboard`) are reachable with
`infra.GetResource(id)` / `infra.GetResources(type)`. Container apps are only present for compute resources that target the
environment. The callback runs last in `ContributeResourcesAsync`, after the apps/jobs have been added.

## Example

```csharp
builder.AddPulumiAzureContainerAppEnvironment("aca-env")
    .ConfigureInfrastructure(infra =>
    {
        infra.ContainerAppManagedEnvironment.Set("zoneRedundant", true);
        infra.GetContainerApp("api")
            .Set("configuration.ingress.allowInsecure", false)
            .Set("configuration.maxInactiveRevisions", 3);
    });
```

Paths follow the azure-native `ManagedEnvironment` / `ContainerApp` / `Job` input properties (verified against
`pulumi package get-schema azure-native`; for a job use e.g. `configuration.replicaTimeout`).
Values may be literals, `PulumiExpr` results or `${res.prop}` references (the `Output<T>` equivalent).

## Unsupported / excluded

- Compile-time types: upstream exposes the `Azure.Provisioning.AppContainers` model classes; here properties are an untyped
  bag addressed by azure-native schema names, since the pipeline emits Pulumi YAML (a typo fails at `pulumi up`, not at compile time).
- Polyglot / ATS projection: the callback is C# only; there is no ATS equivalent for Pulumi.
- Experimental opt-in gate (`ASPIREAZUREPROVISIONING001`): not replicated; the API is always available.
- `BicepValue<T>` operators and ternaries: Pulumi YAML has none, resolve conditions in .NET.
