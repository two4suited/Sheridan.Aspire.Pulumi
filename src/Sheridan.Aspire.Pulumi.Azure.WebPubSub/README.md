# Sheridan.Aspire.Pulumi.Azure.WebPubSub

Azure Web PubSub provisioned by Pulumi, a drop-in for `AddAzureWebPubSub`.

`AddPulumiWebPubSub` provisions a Web PubSub service with the official shape (SKU `Free_F1`, local auth disabled) and the hubs you add.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.WebPubSub
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");   // Sheridan.Aspire.Pulumi.Azure.AppContainers

var pubsub = builder.AddPulumiWebPubSub("pubsub");
pubsub.AddHub("notifications")
    .AddEventHandler("https://example.com/api/{event}", "*", ["connected"]);

builder.AddProject<Projects.MyApi>("api")
    .WithReference(pubsub)
    .WithComputeEnvironment(acaEnv);

builder.Build().Run();
```

## Features

- `AddHub(name, hubName?)` and `AddEventHandler(urlTemplate, userEventPattern, systemEvents)`; `WithSku(...)` overrides the SKU.
- `WithReference` of the service or a hub grants the compute environment's identity *Web PubSub Service Owner*; `WithRoleAssignments(pubsub, AzureWebPubSubRole....)` replaces it.
- Alias: `AddPulumiAzureWebPubSub`.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.WebPubSub`](https://www.nuget.org/packages/Aspire.Hosting.Azure.WebPubSub) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
