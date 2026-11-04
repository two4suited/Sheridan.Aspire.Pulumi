# Sheridan.Aspire.Pulumi.Azure.ApplicationInsights

Application Insights provisioned by Pulumi, a drop-in for `AddAzureApplicationInsights`.

`AddPulumiApplicationInsights` provisions a `web` Application Insights component backed by a Log Analytics workspace. Without a workspace, a PerGB2018 workspace is created in the same resource group, as the official Bicep does.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.ApplicationInsights
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var logs = builder.AddPulumiLogAnalyticsWorkspace("logs");   // Sheridan.Aspire.Pulumi.Azure.OperationalInsights
var insights = builder.AddPulumiApplicationInsights("appinsights")
    .WithLogAnalyticsWorkspace(logs);

builder.AddProject<Projects.MyApi>("api")
    .WithReference(insights);   // ConnectionStrings__appinsights

builder.Build().Run();
```

## Features

- Outputs: `appInsightsConnectionString`, `name`, `id`. The connection string is what `WithReference` injects.
- `WithLogAnalyticsWorkspace(law)` links an existing workspace bound to the same stack.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.ApplicationInsights`](https://www.nuget.org/packages/Aspire.Hosting.Azure.ApplicationInsights) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
