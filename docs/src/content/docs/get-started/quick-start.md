---
title: Quick start
description: Install the packages and deploy an Aspire app with Pulumi.
---

## Install packages

Packages are published to [nuget.org](https://www.nuget.org/profiles/two4suited) when a `vX.Y.Z` tag is pushed, so no extra feed is needed:

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.AppContainers
dotnet add package Sheridan.Aspire.Pulumi.Azure.CosmosDB
```

## Write your AppHost

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");
var cosmos = builder.AddPulumiCosmosDB("cosmos");
cosmos.AddCosmosDatabase("appdb").AddContainer("items", "/id");

builder.AddProject<Projects.MyApi>("api")
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(acaEnv)
    .WithReference(cosmos);

builder.Build().Run();
```

## Publish or deploy

```bash
aspire publish   # generate the Pulumi program
aspire deploy    # deploy it
```

`aspire publish` writes `Pulumi.yaml` and `Pulumi.<stack>.yaml`; `aspire deploy` checks `pulumi whoami`, creates the ESC environment and stack if missing, and runs `pulumi up`. See [How it works](/Sheridan.Aspire.Pulumi/concepts/how-it-works/).
