---
title: Quick start
description: Install the packages and deploy an Aspire app with Pulumi.
---

## Add the package feed

Packages are published to GitHub Packages (NuGet) when a `vX.Y.Z` tag is pushed. Add the feed with a token that has `read:packages`:

```bash
dotnet nuget add source "https://nuget.pkg.github.com/two4suited/index.json" --name github-two4suited --username <github-user> --password <token> --store-password-in-clear-text
```

## Install packages

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
