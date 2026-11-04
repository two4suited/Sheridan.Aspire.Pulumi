# Sheridan.Aspire.Pulumi.Azure.EntraId

Entra ID application registrations with the same API as the official `Aspire.Hosting.Azure.EntraId` package, provisioned by Pulumi.

`AddPulumiEntraId` registers an application and its service principal, and `WithReference` injects the settings your app needs to sign users in (`AzureAd__Instance`, `AzureAd__TenantId`, `AzureAd__ClientId` for .NET projects).

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.EntraId
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var entra = builder.AddPulumiEntraId("entra")
    .WithSignInAudience(EntraIdSignInAudience.AzureADMyOrg)
    .WithRedirectUris("https://localhost:5001/signin-oidc")
    .WithIdTokenIssuance();

builder.AddProject<Projects.MyWeb>("web")
    .WithReference(entra);

builder.Build().Run();
```

## Features

- `WithSignInAudience`, `WithRedirectUris`, `WithIdTokenIssuance` and `WithInstance` configure the registration.
- `WithApplicationOwners`, `WithServicePrincipalOwners` and `WithOwners` add owners by object id.
- `WithDisplayName` pins the display name instead of `{app}-{resource}-{stack}`.
- `AddPulumiExistingEntraApplication(name, tenantId, clientId)` / `AsExistingApplication(...)` reference a registration that already exists; nothing is provisioned.
- `WithReference(entra)` injects configuration as environment variables: Microsoft.Identity.Web names for .NET projects, an uppercase resource-name prefix for other resources.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.EntraId`](https://www.nuget.org/packages/Aspire.Hosting.Azure.EntraId) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/provisioning.md).
- Resource naming: [naming guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/naming.md).
