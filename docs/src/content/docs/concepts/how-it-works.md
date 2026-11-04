---
title: How it works
description: The Pulumi pipeline model behind AddPulumiStack.
sidebar:
  order: 1
---

`AddPulumiStack` declares a Pulumi stack in your AppHost. There is no separate Pulumi project: the Aspire pipeline generates it.

- `aspire publish` writes `Pulumi.yaml` and `Pulumi.<stack>.yaml` (which imports the ESC environment) to `<output>/<name>/pulumi/`.
- `aspire deploy` checks `pulumi whoami`, creates the ESC environment and stack if missing, and runs `pulumi up`. State lives in Pulumi Cloud.
- `aspire destroy` (or `aspire do pulumi-destroy-<name>`) runs `pulumi destroy`.

## Stack and ESC environment

The stack name defaults to the lower-cased Aspire environment, so `aspire deploy -e prod` targets stack `prod`; pass a second argument to `AddPulumiStack` to pin it. Both stack and ESC environments can be overridden on the command line:

```bash
aspire deploy -- --Pulumi:Stack=qa --Pulumi:Esc=shared/qa,myproj/qa
```

They can also be set via config or the `Pulumi__Stack` / `Pulumi__Esc` environment variables. Use `.WithEscEnvironment("project/env1", "project/env2")` to import existing ESC environments (later ones override earlier ones); with none specified, `<name>/<stack>` is used and created on deploy if missing.

For how deploys are ordered, see the [core package](/Sheridan.Aspire.Pulumi/integrations/core/sheridan-aspire-pulumi/).
