# Sheridan.Aspire.Pulumi.Provisioning

Customize the Pulumi resources an integration generates with `ConfigureInfrastructure`, the Pulumi counterpart of Azure.Provisioning.

Official Aspire integrations let you edit the generated Bicep through typed `Azure.Provisioning` models. This package provides the equivalent for Pulumi: a `PulumiInfrastructure` object handed to your callback, from which you set any azure-native property by its schema path.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Provisioning
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var search = builder.AddPulumiSearch("search")   // Sheridan.Aspire.Pulumi.Azure.Search
    .ConfigureInfrastructure(infra =>
    {
        infra.SearchService.Set("replicaCount", 3);
        infra.SearchService.Set("properties.publicNetworkAccess", "Disabled");
    });

builder.Build().Run();
```

## Features

- `PulumiInfrastructure` exposes the resources a contributor added (`Resources`, `GetResource(id)`, `TryGetResource(id)`, lookup by type token).
- `PulumiResourceBuilder` / `PulumiResourceNode` set properties by dotted azure-native schema names (for example `properties.sku.name`).
- `PulumiExpr` builds Pulumi expressions (`${resource.output}`, `fn::` functions) for values that depend on other resources.
- Callbacks run last in each resource's contribution, so they see (and can override) everything the integration generated.

## Notes

- Properties are untyped dotted paths: a typo surfaces at `pulumi preview` / `pulumi up`, not at compile time. Check names with `pulumi package get-schema azure-native`.
- Integration packages already reference this package; you only add it yourself to write your own integrations.

## Learn more

- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/provisioning.md).
- Resource naming: [naming guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/naming.md).
