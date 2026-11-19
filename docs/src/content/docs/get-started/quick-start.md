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

builder.AddDotnetProject("api", "../MyApi/MyApi.csproj")
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(acaEnv)
    .WithReference(cosmos);

builder.Build().Run();
```

`AddDotnetProject` comes from the `Aspire.Hosting.Dotnet` package (Aspire 17) and replaces the now-obsolete `AddProject<Projects.X>`; the path is relative to the AppHost project.

## Publish or deploy

```bash
aspire publish   # generate the Pulumi program
aspire deploy    # deploy it
```

`aspire publish` writes `Pulumi.yaml` and `Pulumi.<stack>.yaml`; `aspire deploy` checks `pulumi whoami`, creates the ESC environment and stack if missing, and runs `pulumi up` (with a local backend, `whoami` and ESC are skipped). See [How it works](/Sheridan.Aspire.Pulumi/concepts/how-it-works/).
