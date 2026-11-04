# Sheridan.Aspire.Pulumi.Azure.CognitiveServices

Azure OpenAI (Cognitive Services) provisioned by Pulumi, a drop-in for `AddAzureOpenAI`.

`AddPulumiOpenAI` provisions an `OpenAI` Cognitive Services account (S0, local auth disabled) and the model deployments you add.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.CognitiveServices
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");   // Sheridan.Aspire.Pulumi.Azure.AppContainers

var openai = builder.AddPulumiOpenAI("openai");
var chat = openai.AddDeployment("chat", "gpt-4o", "2024-08-06");

builder.AddProject<Projects.MyApi>("api")
    .WithComputeEnvironment(acaEnv)
    .WithReference(chat);   // Endpoint=<endpoint>;Deployment=chat

builder.Build().Run();
```

## Features

- `AddDeployment(name, model, version)` with `WithProperties` for `DeploymentName`, `SkuName`, `SkuCapacity` (defaults Standard / 8); deployments are chained with `dependsOn` as in the official Bicep.
- `WithReference(openai)` or `WithReference(deployment)` grants the compute environment's identity *Cognitive Services OpenAI User*; `WithRoleAssignments(openai, AzureOpenAIRole....)` replaces that.
- Aliases: `AddPulumiAzureOpenAI`, `AddPulumiCognitiveServices`.

## Notes

- The custom sub-domain is `<name><first segment of the subscription id>` (override with `CustomSubDomainName`) instead of Bicep's `uniqueString`.
- Not ported: private endpoint / network security perimeter association and `PublishAsExisting`.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.CognitiveServices`](https://www.nuget.org/packages/Aspire.Hosting.Azure.CognitiveServices) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
