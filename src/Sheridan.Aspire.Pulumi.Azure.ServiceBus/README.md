# Sheridan.Aspire.Pulumi.Azure.ServiceBus

Azure Service Bus provisioned by Pulumi, a drop-in for `AddAzureServiceBus`.

`AddPulumiServiceBus` provisions a Standard-SKU namespace with local auth disabled, plus the queues, topics and subscriptions you add.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.ServiceBus
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");   // Sheridan.Aspire.Pulumi.Azure.AppContainers

var sb = builder.AddPulumiServiceBus("messaging");
sb.AddServiceBusQueue("orders").WithProperties(q => q.MaxDeliveryCount = 5);
sb.AddServiceBusTopic("events").AddServiceBusSubscription("audit");

builder.AddProject<Projects.MyApi>("api")
    .WithReference(sb)
    .WithComputeEnvironment(acaEnv);

builder.Build().Run();
```

## Features

- `AddServiceBusQueue` / `AddServiceBusTopic` / `AddServiceBusSubscription` (and the `AddQueue` / `AddTopic` / `AddSubscription` forms) with `WithProperties(...)`, including correlation-filter `Rules`.
- `WithReference` of the namespace, a queue, a topic or a subscription grants the compute environment's identity *Azure Service Bus Data Owner*; `WithRoleAssignments(sb, AzureServiceBusRole....)` replaces it.
- Alias: `AddPulumiAzureServiceBus`.

## Using an existing namespace

`AsExisting` (alias `PublishAsExisting`) looks up a namespace created elsewhere (e.g. by another Pulumi stack) with `azure-native:servicebus:getNamespace` instead of creating one. Queues, topics, subscriptions and role assignments are still created inside it, and the endpoint/name outputs work as usual, so `WithReference` and `WithRoleAssignments` are unchanged.

```csharp
var sb = builder.AddPulumiServiceBus("messaging")
    .AsExisting("shared-sb", "shared-rg");   // name, resource group
sb.AddServiceBusQueue("orders");
```

`resourceGroupName` is optional; when omitted the resource group configured for the resource (`WithResourceGroup` / `WithExistingResourceGroup`) is used. `ConfigureInfrastructure(...).ServiceBusNamespace` throws for an existing namespace, and `WithSku` is ignored.

## Notes

- Not ported: `RunAsEmulator` (the Service Bus emulator) and its configuration APIs.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.ServiceBus`](https://www.nuget.org/packages/Aspire.Hosting.Azure.ServiceBus) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
