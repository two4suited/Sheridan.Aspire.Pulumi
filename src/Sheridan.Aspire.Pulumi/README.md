# Sheridan.Aspire.Pulumi

The core of the Sheridan.Aspire.Pulumi family: turns an Aspire AppHost into a Pulumi program and drives `pulumi up` / `pulumi destroy` from the Aspire pipeline.

`AddPulumiStack` declares a Pulumi stack in your AppHost. There is no separate Pulumi project to maintain: `aspire publish` generates `Pulumi.yaml` and `Pulumi.<stack>.yaml` from the resources bound to the stack, and `aspire deploy` runs `pulumi up` (creating the ESC environment and stack if missing). Resource packages such as `Sheridan.Aspire.Pulumi.Azure.CosmosDB` contribute resources to the stack.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

builder.AddPulumiStack("my-app", "dev")
    .WithOrganization("my-org")
    .WithEscEnvironment("my-app/dev");   // optional; defaults to <name>/<stack>

// Add resources from the other packages, e.g.:
// var cosmos = builder.AddPulumiCosmosDB("cosmos");

builder.Build().Run();
```

## Features

- `PulumiStackResource`, `PulumiProgram`, `PulumiCli` and `PulumiOutputReference`: the stack, the generated program, the Pulumi CLI wrapper and cross-resource output references.
- `AddPulumiStack(name, stack?)`, `WithOrganization`, `WithProjectName`, `WithEscEnvironment`, `WithLocalBackend`, `WithConfig`, `WithProviderConfig` and `DependsOnStack`.
- Pipeline steps for `aspire publish`, `aspire deploy` and `aspire destroy` (`pulumi whoami` check, ESC/stack creation, `pulumi up`, `pulumi destroy`). State lives in Pulumi Cloud unless a local backend is used (see below).
- Multiple stacks: bind each resource to its stack with `WithPulumiStack(...)` and order stacks with `DependsOnStack(...)` or simply by referencing resources across them.
- Command-line overrides: `aspire deploy -- --Pulumi:Stack=qa --Pulumi:Esc=shared/qa` (also `Pulumi__Stack` / `Pulumi__Esc` environment variables).

## Local backend (no Pulumi Cloud, no ESC)

`aspire deploy` / `aspire destroy` work against a self-managed Pulumi backend, e.g. for an emulator:

```csharp
builder.AddPulumiStack("my-app", "dev")
    .WithLocalBackend("/path/to/state")                                // or omit to rely on PULUMI_BACKEND_URL / `pulumi login file://...`
    .WithProviderConfig("azure-native", "metadataHost", "localhost:4577")   // = WithConfig("azure-native:metadataHost", ...)
    .WithConfig("app:apiKey", builder.Configuration["ApiKey"]!, secret: true);                        // encrypted secret config
```

When the backend is not Pulumi Cloud (`WithLocalBackend`, `PULUMI_BACKEND_URL`, or a current `pulumi login` to a `file://`, `s3://`, `azblob://` or `gs://` URL), the pipeline skips `pulumi whoami` and the ESC environment create/import (no `environment:` entry is written to `Pulumi.<stack>.yaml`), creates the stack with `pulumi stack select` / `pulumi stack init`, and ignores `WithOrganization`. Set `PULUMI_CONFIG_PASSPHRASE` (empty string for none) for the passphrase secrets provider. `WithLocalBackend(path)` sets `PULUMI_BACKEND_URL` on this stack's `pulumi` child processes only (so stacks can use different backends); relative paths are made absolute; the default is `file://~`. `WithConfig(key, value)` writes plain config under `config:` in `Pulumi.<stack>.yaml` for any backend, and an existing `encryptionsalt` is preserved when the file is regenerated. `WithConfig(key, value, secret: true)` never writes the value to the generated file: after the stack is selected/created (provision, deploy and destroy steps) the pipeline runs `pulumi config set --secret key` with the value on stdin, so Pulumi stores it encrypted with the stack's secrets provider (only during `aspire deploy`/`destroy`, so the stack file from a plain `aspire publish` has no secrets; values are passed as-is, a trailing newline may be trimmed by Pulumi; stack-file config overrides ESC `pulumiConfig`; re-applied each time the file is regenerated; with the passphrase provider `PULUMI_CONFIG_PASSPHRASE` must be set). `WithProviderConfig(provider, key, value, secret: false)` is shorthand that sets `<provider>:<key>`.

## Run mode

When the AppHost runs locally (`aspire start` / F5), deploy-only resources with no local counterpart (the Pulumi stack, resource groups, container registry and Container Apps environment) are hidden from the dashboard and `aspire describe`. They remain in the model for `aspire publish` / `aspire deploy`. Resources with a local emulator (Cosmos DB, Storage, Service Bus) stay visible. To hide your own deploy-only resource the same way, call `HideInRunMode()` on its builder.

## How `aspire deploy` orders things

Each stack contributes `pulumi-publish` → `pulumi-provision` → `pulumi-deploy` steps. The generated program is split in two halves: **app resources** (container apps, web apps, Kubernetes workloads) and **infrastructure** (everything else).

1. `pulumi-provision` runs `pulumi up --target` for all infrastructure, so the outputs that apps consume (endpoints, hosts, ...) exist.
2. Images are built and pushed to the registry created in step 1.
3. `pulumi-deploy` regenerates the program, now that the outputs resolve, and runs the full `pulumi up` to create the apps.

Infrastructure that (transitively) references an app, such as an API Management backend whose URL is a container app's ingress FQDN and the API policy ordered after that backend, belongs to the app half: it is neither targeted nor part of the program the provision step runs, and is created by `pulumi-deploy` together with the app. (Pulumi YAML evaluates untargeted resources too, and a nested property of an app that does not exist yet is nil, so leaving it untargeted alone would fail the first deploy.) On a redeploy those resources are already in state, and Pulumi refuses to delete a resource that is missing from the program but not targeted, so the provision step reads the stack state and targets them: when the apps they depend on are in state they stay in the program and are updated in place; only the ones that depend on an app missing from state (a newly added app, or one whose type changed) are left out of the program and removed, and `pulumi-deploy` recreates them. Stack outputs that reference such a resource are likewise only set by `pulumi-deploy`, so they stay missing if the deploy step fails after the provision step removed them. `aspire publish` always writes the full program.

When a resource in stack *S* references or waits for a resource in stack *T*, *S*'s provision step automatically depends on *T*'s deploy step. `DependsOnStack` is only needed for dependencies Aspire cannot see.

## Learn more

- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
