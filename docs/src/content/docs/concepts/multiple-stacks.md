---
title: Multiple stacks
description: Split resources across Pulumi stacks with ordered deploys.
sidebar:
  order: 2
---

Split resources across Pulumi stacks (own project directory, own ESC environment) by adding more than one `AddPulumiStack` and binding each resource with `.WithPulumiStack(...)`:

```csharp
var identityStack = builder.AddPulumiStack("my-app-identity", "identity-dev")
    .WithProjectName("my-app")
    .WithOrganization("my-org")
    .WithEscEnvironment("identity/dev");

builder.AddPulumiAzureAdAppRegistration("app-registration")
    .WithPulumiStack(identityStack);

var azureStack = builder.AddPulumiStack("my-app", "azure-dev")
    .WithProjectName("my-app")
    .WithOrganization("my-org")
    .WithEscEnvironment("azure/dev")
    .DependsOnStack(identityStack);

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env")
    .WithPulumiStack(azureStack);
```

- With more than one stack in the model, every stack-bound resource must call `.WithPulumiStack(...)` explicitly.
- `.WithProjectName(...)` groups several stacks under one Pulumi project in the backend (here `my-org/my-app/identity-dev` and `my-org/my-app/azure-dev`, so stack names must differ); it defaults to the Aspire resource name.
- `.DependsOnStack(...)` makes `aspire deploy` fully deploy the dependency before provisioning this stack. It only orders the provision/deploy steps and has no effect on `aspire publish`. References between resources in different stacks create the same ordering automatically.
