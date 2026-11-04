# Sheridan.Aspire.Pulumi.Azure.Sandboxes

_Container Apps Sandboxes_ — part of [Sheridan.Aspire.Pulumi](../../README.md).

`Sheridan.Aspire.Pulumi.Azure.Sandboxes` ports `Aspire.Hosting.Azure.Sandboxes` (preview upstream). `builder.AddPulumiSandboxGroup("sandboxes")` (aliases `AddPulumiAzureSandboxGroup`, `AddPulumiSandboxes`) is a compute environment, like `AddAzureSandboxGroup`. Compute resources that target it are built (linux/amd64), pushed to the group's registry and deployed as sandboxes.

It is split along the line where Pulumi stops being able to manage things:

- **Pulumi (ARM) half**: the sandbox group, a Basic container registry (`sandboxes-acr`), a dedicated image-pull identity (`sandboxes-mi`) with *AcrPull* on it, and *Container Apps SandboxGroup Data Owner* for the deploying principal (outputs `id`, `name`, `location`, `resourceGroup`, `subscriptionId`, `imagePullIdentityClientId`, registry name and login server).
- **Pipeline (data plane) half**: the disk images, sandboxes, lifecycle policies and ports are not ARM resources. They live on the Azure Dev Compute data plane (`https://management.{region}.azuredevcompute.io`), which neither azure-native nor azapi models (azapi's data-plane resource only supports a fixed list of services, and a Pulumi dynamic provider cannot be used from the YAML runtime). So, exactly like the official integration, a `sandbox-deploy-{resource}` pipeline step calls that API after `pulumi up`: it resolves the pushed image to its immutable digest, creates the disk image from it (pulled with the group's identity), waits until it is ready, creates the sandbox (tier, environment variables, command, egress policy, workload identity), applies the lifecycle policy and exposes the ports. It authenticates with `DefaultAzureCredential` (e.g. `az login`) and retries 403s while the role assignment propagates. The previous generation of a sandbox is removed once the new one is up, and `aspire destroy` removes every sandbox and disk image this stack deployed to the group before `pulumi destroy` deletes it.

```csharp
var sandboxes = builder.AddPulumiSandboxGroup("sandboxes")
    .WithPulumiStack(azureStack)
    .WithSystemAssignedIdentity()                                   // or WithUserAssignedIdentity(identity) / WithNoManagedIdentity()
    // .WithAzureContainerRegistry(registry)                        // use your own Pulumi ACR instead of sandboxes-acr
    // .WithAcrPullIdentity(identity)                               // you then grant AcrPull yourself, as upstream
    // .PublishAsExisting("my-group", "my-rg")                      // existing group; needs WithAcrPullIdentity, and you grant the Data Owner role
    .ConfigureInfrastructure(infra => infra.SandboxGroup.Set("tags.team", "platform"));

var api = builder.AddProject<Projects.Api>("api")
    .WithExternalHttpEndpoints()
    .PublishAsAzureSandbox(new AzureSandboxOptions                  // optional; defaults to the Medium tier
    {
        Tier = AzureSandboxTier.Small,
        AutoSuspendEnabled = true, AutoSuspendInterval = TimeSpan.FromMinutes(15), AutoSuspendMode = AzureSandboxAutoSuspendMode.Disk,
        Endpoints = [new AzureSandboxEndpointOptions { Name = "http", Anonymous = true }],   // ports use Entra ID auth unless Anonymous
    });
```

`PublishAsAzureSandbox` also has an `Action<AzureSandboxOptions>` overload and is a no-op outside publish mode. `WithSandboxIdentity(identity)` runs a compute resource's sandbox as a Pulumi user-assigned identity (the identity is attached to the group and `AZURE_CLIENT_ID` is set, like `WithAzureUserAssignedIdentity` upstream). When a group is the only compute environment, resources target it automatically; otherwise call `WithComputeEnvironment`. Endpoints of one sandbox can be referenced from another (`WithReference`); the referencing sandbox is deployed after it and receives the public URL. Sandbox egress is deny-by-default and opened only for the hosts that appear in the sandbox's resolved environment variables and arguments (URLs and connection strings), as upstream. The deployment dashboard URL (`https://sandboxes.azure.com/sandbox-groups/...`) is added to the deployment summary.

**Resource provider.** Upstream's Bicep creates `Microsoft.App/sandboxGroups@2026-02-01-preview`. azure-native 3.29 only exposes `azure-native:containerinstance/v20260701:SandboxGroup`, which PUTs to `Microsoft.ContainerInstance/sandboxGroups`: that type does not exist in the Microsoft.ContainerInstance provider (`az provider show -n Microsoft.ContainerInstance` lists no sandbox types, while Microsoft.App lists `sandboxGroups` for `2026-07-01` and `2026-02-01-preview`), and the *Data Owner* role's data actions are `Microsoft.App/sandboxGroups/*`. So the group is created through azure-native's generic ARM resource (`azure-native:resources:Resource`, namespace `Microsoft.App`, type `sandboxGroups`, api-version `2026-02-01-preview`; override with `ConfigureInfrastructure(infra => infra.SandboxGroup.Set("apiVersion", "2026-07-01"))`). Its settings therefore sit under `properties.` as in the REST API. The group is named after the Aspire resource (ARM names are unique per resource group, so upstream's `uniqueString` suffix is not needed).

Limitations (all verified only against `pulumi preview` and `aspire do diagnostics`; nothing was deployed because it needs a subscription with Sandboxes preview access and incurs cost):

- The data-plane steps are pipeline code, not Pulumi resources: `pulumi up` shows only the group, registry and identities, and the sandboxes are not tracked in Pulumi state. Upstream keeps a local deployment-state file with a cross-process lease and retains the previous URL while rolling; this port tracks ownership with sandbox/disk image labels (`aspire-owner`, `aspire-resource`, `aspire-deploy`), deletes the previous generation after the new one is up, and does not garbage-collect sandboxes of resources removed from the AppHost until `aspire destroy`.
- Requires a container runtime that can inspect image manifests/config (Docker or Podman) and `az` for `acr login`.
- Container mounts and non-HTTP endpoints are rejected (as upstream). Sandbox URLs are only known after deployment, so they cannot feed Pulumi-provisioned values.
