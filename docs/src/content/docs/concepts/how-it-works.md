---
title: How it works
description: The Pulumi pipeline model behind AddPulumiStack.
sidebar:
  order: 1
---

`AddPulumiStack` declares a Pulumi stack in your AppHost. There is no separate Pulumi project: the Aspire pipeline generates it.

- `aspire publish` writes `Pulumi.yaml` and `Pulumi.<stack>.yaml` (which imports the ESC environment) to `<output>/<name>/pulumi/`.
- `aspire deploy` checks `pulumi whoami`, creates the ESC environment and stack if missing, and runs `pulumi up`. State lives in Pulumi Cloud unless you use a [local backend](#local-backend).
- `aspire destroy` (or `aspire do pulumi-destroy-<name>`) runs `pulumi destroy`.

Each stack contributes `pulumi-publish` → `pulumi-provision` → `pulumi-deploy` steps. The provision step first creates the infrastructure that apps consume (registry, environment, ...); the deploy step then regenerates the program and creates the apps. On a redeploy, resources that are already in state but no longer in the model, or that depend on an app, are targeted too so Pulumi can delete or update them instead of aborting.

## Stack and ESC environment

The stack name defaults to the lower-cased Aspire environment, so `aspire deploy -e prod` targets stack `prod`; pass a second argument to `AddPulumiStack` to pin it. Both stack and ESC environments can be overridden on the command line:

```bash
aspire deploy -- --Pulumi:Stack=qa --Pulumi:Esc=shared/qa,myproj/qa
```

They can also be set via config or the `Pulumi__Stack` / `Pulumi__Esc` environment variables. Use `.WithEscEnvironment("project/env1", "project/env2")` to import existing ESC environments (later ones override earlier ones); with none specified, `<name>/<stack>` is used and created on deploy if missing.

For how deploys are ordered, see the [core package](/Sheridan.Aspire.Pulumi/integrations/core/sheridan-aspire-pulumi/).

## Local backend

To deploy without Pulumi Cloud or ESC (for example against an emulator), use a self-managed backend and plain stack config:

```csharp
builder.AddPulumiStack("my-app", "dev")
    .WithLocalBackend("/path/to/state")
    .WithConfig("azure-native:metadataHost", "localhost:4577");
```

With a non-Pulumi-Cloud backend (`WithLocalBackend`, `PULUMI_BACKEND_URL`, or a current `pulumi login` to a `file://`, `s3://`, `azblob://` or `gs://` URL) the pipeline skips `pulumi whoami` and ESC, creates the stack with `pulumi stack select` / `init`, and ignores `WithOrganization`. `WithConfig(key, value)` writes plain config to `Pulumi.<stack>.yaml` for any backend.

## Run mode

When the AppHost runs locally (`aspire start` / F5), deploy-only resources with no local counterpart (the stack, resource groups, container registry, Container Apps environment, AKS and network resources) are hidden from the dashboard. They stay in the model for `aspire publish` / `aspire deploy`; call `HideInRunMode()` to hide your own.
