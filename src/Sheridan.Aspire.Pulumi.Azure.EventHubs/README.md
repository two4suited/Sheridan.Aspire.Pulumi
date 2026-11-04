# Sheridan.Aspire.Pulumi.Azure.EventHubs

Azure Event Hubs provisioned by Pulumi, a drop-in for `AddAzureEventHubs`.

`AddPulumiEventHubs` provisions an Event Hubs namespace with the hubs and consumer groups you add.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.EventHubs
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");   // Sheridan.Aspire.Pulumi.Azure.AppContainers

var eventHubs = builder.AddPulumiEventHubs("events");
var hub = eventHubs.AddHub("telemetry");
hub.AddConsumerGroup("processor");

builder.AddProject<Projects.MyApi>("api")
    .WithComputeEnvironment(acaEnv)
    .WithReference(eventHubs);

builder.Build().Run();
```

## Features

- `AddHub(name, hubName?)` and `AddConsumerGroup(name, groupName?)`; `WithProperties(...)` tunes a hub.
- `WithSku` sets the namespace SKU; `WithRoleAssignments(...)` uses `AzureEventHubsRole`.
- Alias: `AddPulumiAzureEventHubs` (and `AddEventHub` for hubs).

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.EventHubs`](https://www.nuget.org/packages/Aspire.Hosting.Azure.EventHubs) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
