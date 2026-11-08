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

`resourceGroupName` is optional, but a group must be resolvable: when omitted, call `WithExistingResourceGroup` / `WithResourceGroup`, otherwise program generation throws. A group passed to `AsExisting` is always the one used for the lookup and the child entities. `ConfigureInfrastructure(...).ServiceBusNamespace` throws for an existing namespace, and `WithSku` is ignored.

## Running locally

Connection values normally come from the deployed stack's outputs, so `aspire run` fails with "Pulumi project ... has not been generated yet" until something is deployed. Two ways around that:

### Emulator

```csharp
var sb = builder.AddPulumiServiceBus("messaging").RunAsEmulator();
sb.AddServiceBusQueue("orders");
sb.AddServiceBusTopic("events").AddServiceBusSubscription("audit");
```

`RunAsEmulator` (run mode only; no effect when publishing) starts the Service Bus emulator plus the SQL Server container it stores state in. Queues, topics, subscriptions and rules declared on the resource are written to the emulator's `Config.json`, and `WithReference` resolves to the emulator's connection string, so no Pulumi output is read. The optional callback configures the emulator container:

- `WithHostPort(int?)` pins the AMQP port.
- `WithConfigurationFile(path)` supplies your own `Config.json`; `WithConfiguration(json => ...)` edits the generated one.

> **Apple Silicon:** the SQL Server image crashes (segfault) under QEMU. Enable Docker Desktop's *Use Rosetta for x86_64/amd64 emulation*.

### Existing namespace

For an `AsExisting` namespace, `RunAsExisting` supplies the endpoint to use when running locally (a string or a parameter), instead of the deployed stack output:

```csharp
var endpoint = builder.AddParameter("sb-endpoint");
var sb = builder.AddPulumiServiceBus("messaging")
    .AsExisting("shared-sb", "shared-rg")
    .RunAsExisting(endpoint);   // e.g. shared-sb.servicebus.windows.net
```

The namespace has local auth disabled, so the app authenticates with `DefaultAzureCredential`. In publish mode `RunAsExisting` has no effect.

## Notes

- Not ported: the emulator's `WithSqlServer` overloads (the SQL Server container is always created for you).

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.ServiceBus`](https://www.nuget.org/packages/Aspire.Hosting.Azure.ServiceBus) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
