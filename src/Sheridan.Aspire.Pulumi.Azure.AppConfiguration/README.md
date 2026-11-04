# Sheridan.Aspire.Pulumi.Azure.AppConfiguration

Azure App Configuration provisioned by Pulumi, a drop-in for `AddAzureAppConfiguration`.

`AddPulumiAppConfiguration` provisions a standard-SKU configuration store with local auth disabled. The endpoint is the connection string, so `WithReference(appConfig)` yields `ConnectionStrings__appconfig`.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.AppConfiguration
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");   // Sheridan.Aspire.Pulumi.Azure.AppContainers

var appConfig = builder.AddPulumiAppConfiguration("appconfig")
    .RunAsEmulator();   // local emulator container (run mode only)

builder.AddProject<Projects.MyApi>("api")
    .WithComputeEnvironment(acaEnv)
    .WithRoleAssignments(appConfig, AzureAppConfigurationRole.AppConfigurationDataReader)   // default is Data Owner
    .WithReference(appConfig);

builder.Build().Run();
```

## Features

- Outputs: `appConfigEndpoint`, `name`, `id`.
- Emulator options: `WithDataVolume`, `WithDataBindMount`, `WithHostPort`, `WithAnonymousAccess`.
- `PublishAsExisting` / `AsExisting`, and `ConfigureInfrastructure` on the `AppConfigurationStore` root. Alias: `AddPulumiAzureAppConfiguration`.

## Notes

- Not ported: private endpoints and the polyglot (ATS) projections.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.AppConfiguration`](https://www.nuget.org/packages/Aspire.Hosting.Azure.AppConfiguration) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
