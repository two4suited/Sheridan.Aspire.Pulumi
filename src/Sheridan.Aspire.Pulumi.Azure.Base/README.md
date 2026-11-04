# Sheridan.Aspire.Pulumi.Azure.Base

The shared Azure layer: resource groups, user-assigned identities, role definitions and the deterministic naming scheme.

Every Azure integration builds on this package. By default a resource creates its own resource group; use `AddPulumiAzureResourceGroup` and `WithResourceGroup` to share one. Names are derived from the app, resource and stack names, shortened deterministically to fit each resource type's limits.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.Base
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var sharedRg = builder.AddPulumiAzureResourceGroup("shared-rg");

var workloadIdentity = builder.AddPulumiAzureUserAssignedIdentity("workload-identity")
    .WithResourceGroup(sharedRg)
    .WithLocation("westeurope");

builder.Build().Run();
```

## Features

- `AddPulumiAzureResourceGroup`, `WithResourceGroup(rg)` and `WithExistingResourceGroup("name")` for resource groups Pulumi does not manage.
- `AddPulumiAzureUserAssignedIdentity` for user-assigned managed identities.
- `WithLocation` and `WithAzureName(AzureNameKind.X, "name")` to pin a location or a generated name.
- `AzureNaming` / `AzureNamingExtensions` implement the naming scheme; `AzureRoleDefinitions` maps built-in Azure roles.

## Notes

- `WithResourceGroup` is a same-program reference: both resources must be bound to the same Pulumi stack. Binding them to different stacks throws a clear error instead of generating broken YAML.

## Learn more

- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/provisioning.md).
- Resource naming: [naming guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/naming.md).
