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
- `AddPulumiStack(name, stack?)`, `WithOrganization`, `WithProjectName`, `WithEscEnvironment` and `DependsOnStack`.
- Pipeline steps for `aspire publish`, `aspire deploy` and `aspire destroy` (`pulumi whoami` check, ESC/stack creation, `pulumi up`, `pulumi destroy`). State lives in Pulumi Cloud.
- Multiple stacks: bind each resource to its stack with `WithPulumiStack(...)` and order stacks with `DependsOnStack(...)` or simply by referencing resources across them.
- Command-line overrides: `aspire deploy -- --Pulumi:Stack=qa --Pulumi:Esc=shared/qa` (also `Pulumi__Stack` / `Pulumi__Esc` environment variables).

## Run mode

When the AppHost runs locally (`aspire start` / F5), deploy-only resources with no local counterpart (the Pulumi stack, resource groups, container registry and Container Apps environment) are hidden from the dashboard and `aspire describe`. They remain in the model for `aspire publish` / `aspire deploy`. Resources with a local emulator (Cosmos DB, Storage, Service Bus) stay visible. To hide your own deploy-only resource the same way, call `HideInRunMode()` on its builder.

## How `aspire deploy` orders things

Each stack contributes `pulumi-publish` → `pulumi-provision` → `pulumi-deploy` steps. The generated program is split in two halves: **app resources** (container apps, web apps, Kubernetes workloads) and **infrastructure** (everything else).

1. `pulumi-provision` runs `pulumi up --target` for all infrastructure, so the outputs that apps consume (endpoints, hosts, ...) exist.
2. Images are built and pushed to the registry created in step 1.
3. `pulumi-deploy` regenerates the program, now that the outputs resolve, and runs the full `pulumi up` to create the apps.

Infrastructure that (transitively) references an app, such as an API Management backend whose URL is a container app's ingress FQDN and the API policy ordered after that backend, belongs to the app half: it is neither targeted nor part of the program the provision step runs, and is created by `pulumi-deploy` together with the app. (Pulumi YAML evaluates untargeted resources too, and a nested property of an app that does not exist yet is nil, so leaving it untargeted alone would fail the first deploy.) Stack outputs that reference such a resource are likewise only set by `pulumi-deploy`: on a redeploy they are removed by the provision step and set again by the deploy step, so they stay missing if the deploy step fails. `aspire publish` always writes the full program.

When a resource in stack *S* references or waits for a resource in stack *T*, *S*'s provision step automatically depends on *T*'s deploy step. `DependsOnStack` is only needed for dependencies Aspire cannot see.

## Learn more

- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
