# Sheridan.Aspire.Pulumi.Azure.AppService

Azure App Service environment provisioned by Pulumi, a drop-in for `AddAzureAppServiceEnvironment`.

`AddPulumiAzureAppServiceEnvironment` is a compute environment that deploys your projects as web apps on an App Service plan, with a registry, identities and optional dashboard and Application Insights.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.AppService
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var appSvc = builder.AddPulumiAzureAppServiceEnvironment("appsvc")
    .WithSku("P1V3", "Premium")
    .WithDashboard(false);

builder.AddProject<Projects.MyApi>("api")
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(appSvc);

builder.Build().Run();
```

## Features

- `WithSku(skuName, skuTier)` sets the App Service plan SKU (default P0V3 / Premium).
- `WithDashboard` enables or disables the Aspire dashboard site (default enabled).
- `WithAzureApplicationInsights` wires the environment to Application Insights.
- Reachable through `ConfigureInfrastructure`: the registry, dashboard site, Log Analytics, App Insights and identities.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.AppService`](https://www.nuget.org/packages/Aspire.Hosting.Azure.AppService) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/provisioning.md).
- Resource naming: [naming guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/naming.md).
