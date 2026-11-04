# Sheridan.Aspire.Pulumi.Azure.Sandboxes

Azure Container Apps Sandboxes as an Aspire compute environment, provisioned by Pulumi plus a data-plane deployment step.

`AddPulumiSandboxGroup` is a compute environment like `AddAzureSandboxGroup`. Compute resources that target it are built (linux/amd64), pushed to the group's registry and deployed as sandboxes.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.Sandboxes
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var sandboxes = builder.AddPulumiSandboxGroup("sandboxes")
    .WithSystemAssignedIdentity();

var api = builder.AddProject<Projects.MyApi>("api")
    .WithExternalHttpEndpoints()
    .PublishAsAzureSandbox(new AzureSandboxOptions
    {
        Tier = AzureSandboxTier.Small,
        Endpoints = [new AzureSandboxEndpointOptions { Name = "http", Anonymous = true }],
    });

builder.Build().Run();
```

## Features

- Pulumi (ARM) half: the sandbox group, a Basic registry (`sandboxes-acr`), an image-pull identity (`sandboxes-mi`) with AcrPull, and *Container Apps SandboxGroup Data Owner* for the deployer.
- Data-plane half: a `sandbox-deploy-{resource}` pipeline step calls the Azure Dev Compute API after `pulumi up` to create the disk image, sandbox, lifecycle policy and ports.
- Options: `WithSystemAssignedIdentity` / `WithUserAssignedIdentity` / `WithNoManagedIdentity`, `WithAzureContainerRegistry`, `WithAcrPullIdentity`, `PublishAsExisting`, `WithSandboxIdentity`.
- Sandbox egress is deny-by-default and opened only for hosts that appear in the sandbox's environment variables and arguments.

## Notes

- The sandboxes are not tracked in Pulumi state; ownership is tracked with sandbox/disk-image labels and `aspire destroy` removes them before `pulumi destroy`.
- The group is created through azure-native's generic ARM resource (`Microsoft.App/sandboxGroups`, `2026-02-01-preview`) because the typed azure-native resource targets a different provider.
- Requires Docker or Podman and `az` (for `acr login`). Mounts and non-HTTP endpoints are rejected, as upstream.
- Verified with `pulumi preview` only; it needs a subscription with Sandboxes preview access and incurs cost.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.Sandboxes`](https://www.nuget.org/packages/Aspire.Hosting.Azure.Sandboxes) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/provisioning.md).
- Resource naming: [naming guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/naming.md).
