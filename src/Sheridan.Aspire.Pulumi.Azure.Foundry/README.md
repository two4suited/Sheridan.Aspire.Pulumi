# Sheridan.Aspire.Pulumi.Azure.Foundry

Microsoft Foundry (AI Foundry) accounts, projects, deployments, agents and toolboxes provisioned by Pulumi.

`AddPulumiFoundry` ports `AddFoundry` from `Aspire.Hosting.Foundry`: an `AIServices` account with a capability host, model deployments from the catalog, projects with connections, and Foundry data-plane objects (prompt agents and toolboxes) deployed by pipeline steps.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.Foundry
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");   // Sheridan.Aspire.Pulumi.Azure.AppContainers

var foundry = builder.AddPulumiFoundry("foundry");
var chat = foundry.AddDeployment("chat", FoundryModel.OpenAI.Gpt4o);
var project = foundry.AddProject("agents");

var codeInterpreter = project.AddCodeInterpreterTool("code");
project.AddPromptAgent("helper", chat, "You are a helpful assistant.")
    .WithTool(codeInterpreter);

builder.AddProject<Projects.MyApi>("api")
    .WithComputeEnvironment(acaEnv)
    .WithReference(chat);

builder.Build().Run();
```

## Features

- Account: `AIServices` (S0, system-assigned identity, local auth disabled) plus a `foundry-caphost` account capability host, matching the generated Bicep.
- Deployments from `FoundryModel` or by name; projects with Application Insights, `WithKeyVault`, `AddConnection(storage | cosmos | search | containerRegistry)`, `AddBingGroundingConnection` and `AddCapabilityHost(...)`.
- `PublishAsExisting` / `AsExisting`, private endpoints through the Network package, and `RunAsFoundryLocal(endpoint)` to point run mode at a running Foundry Local service.
- `WithReference(foundry | deployment | project)` grants *Cognitive Services User* and *Cognitive Services OpenAI User*; `WithRoleAssignments(foundry, FoundryRole....)` replaces that.
- Prompt agents (`AddPromptAgent`) and toolboxes (`AddToolbox`) are data-plane objects: a `deploy-<name>` step creates or reconciles them through the Foundry APIs after the stack deploys.
- Alias: `AddPulumiAzureAIFoundry`.

## Notes

- Prompt agents and toolboxes are unit tested only, not against a deployed project. The deploying principal needs *Azure AI User* on the project.
- Provider gaps: the project capability host cannot set `capabilityHostKind` / `enablePublicHostingEnvironment`, and the Bing grounding key cannot be read with `listKeys()`, so `AddBingGroundingConnection` takes the key as a secret parameter.
- Not ported: Foundry hosted agents, the Foundry Local model lifecycle, and toolbox run-mode deployment and MCP approval policies.

## Learn more

- Mirrors the official [`Aspire.Hosting.Foundry`](https://www.nuget.org/packages/Aspire.Hosting.Foundry) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/provisioning.md).
- Resource naming: [naming guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/naming.md).
