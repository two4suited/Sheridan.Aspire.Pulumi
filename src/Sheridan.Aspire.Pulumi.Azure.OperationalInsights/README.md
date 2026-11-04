# Sheridan.Aspire.Pulumi.Azure.OperationalInsights

Log Analytics workspaces provisioned by Pulumi, a drop-in for `AddAzureLogAnalyticsWorkspace`.

`AddPulumiLogAnalyticsWorkspace` provisions a workspace you can share between Container Apps environments, App Insights and Kubernetes.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.OperationalInsights
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var law = builder.AddPulumiLogAnalyticsWorkspace("logs")
    .WithLocation("westeurope");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env")
    .WithAzureLogAnalyticsWorkspace(law);

builder.Build().Run();
```

## Features

- `WithLocation`, `WithPulumiStack` and `WithExistingResourceGroup` come from `Sheridan.Aspire.Pulumi.Azure.Base`.
- Use `ConfigureInfrastructure` to adjust the workspace, for example retention or SKU.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.OperationalInsights`](https://www.nuget.org/packages/Aspire.Hosting.Azure.OperationalInsights) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
