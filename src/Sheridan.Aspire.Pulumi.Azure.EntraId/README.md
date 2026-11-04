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
- `WithDisplayName` pins the display name instead of `{app}-{resource}-{stack}`; `WithTags`, `WithDescription` and `WithServiceManagementReference` set metadata.
- `AddPulumiExistingEntraApplication(name, tenantId, clientId)` / `AsExistingApplication(...)` reference a registration that already exists; nothing is provisioned.
  Calls that only make sense for a provisioned registration (redirect URIs, owners, ID-token issuance, roles, permissions, consent, federation, metadata, `ConfigureInfrastructure`) throw `InvalidOperationException` on an existing application, in either call order. Consumer-side metadata (`WithScope`, `WithApplicationIdUri("api://...")`, `WithFederatedManagedIdentityCredential`) stays allowed.
- `WithReference(entra)` injects configuration as environment variables: Microsoft.Identity.Web names for .NET projects, an uppercase resource-name prefix for other resources.

### Exposing an API

```csharp
var api = builder.AddPulumiEntraId("orders-api")
    .WithApplicationIdUri()                                   // api://{clientId}; or WithApplicationIdUri("api://orders")
    .WithScope("Orders.Read", "Read orders")                  // stable GUID derived from the names, or set scope.Id
    .WithAppRole(new EntraIdAppRole("Orders.Read", "Read orders", "Read orders.", ["Application"]));

var web = builder.AddPulumiEntraId("web")
    .WithApiPermissions(api, "Orders.Read")                   // declares only
    .WithApiPermissions(EntraIdApi.MicrosoftGraph, EntraIdApiPermission.Delegated("User.Read"))
    .WithAdminConsent();                                      // the only call that grants anything
api.WithPreAuthorizedApplication(web, "Orders.Read");

api.PublishWithAppRoleAssignment(workerIdentity, "Orders.Read");        // IPulumiManagedIdentityResource
api.PublishWithFederatedManagedIdentityCredential(workerIdentity);      // secretless sign-in with a managed identity
```

`EntraIdApi.MicrosoftGraph`, `AzureResourceManager`, `AzureStorage` and `FromApplicationId(...)` cover APIs that are not in the AppHost; their permission IDs are looked up by name from the tenant at deploy time.

`WithReference` on a .NET project also injects `AzureAd__Scopes` (when the registration exposes scopes), `DownstreamApis__{api}__Scopes__{n}` plus a `{api}AppOnly` entry for APIs it has permissions on, and `ManagedIdentityClientId` (config-only `WithFederatedManagedIdentityCredential(clientId)`). `WithReference(entra, connectionName, downstreamApisConfigKey)` changes the root keys.

### Notes

- Removing an enabled scope or role is rejected by Graph: set `IsEnabled = false`, deploy, then remove it. Removing `PublishWithAppRoleAssignment` or `WithAdminConsent` does not revoke an existing grant.
- Consent and role assignments need `DelegatedPermissionGrant.ReadWrite.All` / `AppRoleAssignment.ReadWrite.All` and `Application.Read.All` on the deploying principal.
- Federated credentials and role assignments reference the identity in the same Pulumi program.
- Not mirrored: the run-mode local developer certificate and Key Vault certificate credentials; see [decision 0002](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/decisions/0002-entraid-upstream-sync.md).
- Customize anything else with `ConfigureInfrastructure(infra => infra.Application.Set(...))`; see the [overlay notes](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/overlays/EntraId.md).

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.EntraId`](https://www.nuget.org/packages/Aspire.Hosting.Azure.EntraId) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/provisioning.md).
- Resource naming: [naming guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/naming.md).
