# Sheridan.Aspire.Pulumi.Azure.AppContainers

_Container Apps environment_ — part of [Sheridan.Aspire.Pulumi](../../README.md).

`builder.AddPulumiAzureContainerAppEnvironment("aca-env")` is the Pulumi equivalent of Aspire's `AddAzureContainerAppEnvironment`. It is a compute environment, so resources opt in the usual way:

```csharp
var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");
builder.AddProject<Projects.MyApi>("api")
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(acaEnv);
```

`aspire publish` writes the resource group, Log Analytics workspace, container registry, pull identity (AcrPull) and managed environment, plus a `ContainerApp` per targeted resource. `aspire deploy` runs: `pulumi-provision` (login, ESC, `pulumi up --target` for all the stack's non-app infrastructure incl. the registry) → `build-<resource>` → `push-<resource>` → `pulumi-deploy` (full `pulumi up`). Images are tagged with a per-deploy timestamp so each deploy creates a new revision.

### How `aspire deploy` orders things (one deploy is enough)

Each `PulumiStackResource` contributes `pulumi-publish` → `pulumi-provision` → `pulumi-deploy` steps. The generated `PulumiProgram` is split in two halves: **app resources** (container apps, web apps, Kubernetes workloads and in-cluster objects, registered with `program.AddAppResource(...)` or inside `program.BeginAppScope()` by the compute environments) and **infrastructure**, which is the default for everything else, so integrations (Cosmos, Storage, Service Bus, Key Vault, SQL, ...) need no changes. Infrastructure that references an app is treated as part of the app half.

1. `pulumi-provision` runs `pulumi up --target` for *every* infrastructure resource (not only the registry), so all stack outputs that apps consume (Cosmos endpoints, storage endpoints, Service Bus hosts, ...) exist.
2. Images are built and pushed (they need the registry from step 1).
3. `pulumi-deploy` regenerates the program — the environment variables that read stack outputs now resolve — and runs the full `pulumi up`, creating the apps.

Aspire relationships drive the ordering between stacks: when a resource hosted in stack *S* references (`WithReference`, an endpoint/connection string/`PulumiOutputReference` in its environment) or `WaitFor`s a resource in stack *T*, *S*'s provision step automatically depends on *T*'s deploy step. `DependsOnStack` is only needed for dependencies Aspire can't see. A deferral (`Deferring environment variable ...`) is logged at debug level while the first generation runs (and in `aspire publish`, which has no real stack) and as a warning only if an output is still missing after the provision pass.
