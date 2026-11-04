# Sheridan.Aspire.Pulumi.Azure.SignalR

Azure SignalR Service provisioned by Pulumi, a drop-in for `AddAzureSignalR`.

`AddPulumiSignalR` provisions an `azure-native:signalrservice:SignalR` with the official shape: kind `SignalR`, SKU `Free_F1`, local auth disabled, CORS `*` and the `ServiceMode` feature. The connection string is `Endpoint=https://{hostName};AuthType=azure`.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.SignalR
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");   // Sheridan.Aspire.Pulumi.Azure.AppContainers

var signalr = builder.AddPulumiSignalR("signalr")
    .WithSku("Standard_S1")   // Pulumi addition; Free_F1 is the default
    .ConfigureInfrastructure(infra =>
        infra.SignalRService.Set("cors.allowedOrigins", new[] { "https://example.com" }));

builder.AddProject<Projects.MyApi>("api")
    .WithReference(signalr)
    .WithComputeEnvironment(acaEnv);

builder.Build().Run();
```

## Features

- `AddPulumiSignalR("name", AzureSignalRServiceMode.Serverless)` selects the service mode.
- `WithReference` grants the compute environment's identity *SignalR App Server*; `WithRoleAssignments(signalr, AzureSignalRRole....)` replaces it.
- Alias: `AddPulumiAzureSignalR`.

## Notes

- Not ported: `RunAsEmulator` and the `SignalRBuiltInRole` (Azure.Provisioning) overload of `WithRoleAssignments`.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.SignalR`](https://www.nuget.org/packages/Aspire.Hosting.Azure.SignalR) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/provisioning.md).
- Resource naming: [naming guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/naming.md).
