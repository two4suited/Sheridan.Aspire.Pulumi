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

builder.AddDotnetProject("web", "../MyWeb/MyWeb.csproj")
    .WithReference(entra);

builder.Build().Run();
```

## Features

- `WithSignInAudience`, `WithRedirectUris`, `WithIdTokenIssuance` and `WithInstance` configure the registration.
- `WithApplicationOwners`, `WithServicePrincipalOwners` and `WithOwners` add owners by object id.
- `WithDisplayName` pins the display name instead of `{app}-{resource}-{stack}`; `WithTags`, `WithDescription` and `WithServiceManagementReference` set metadata.
- `AddPulumiExistingEntraApplication(name, tenantId, clientId)` / `AsExistingApplication(...)` reference a registration that already exists; nothing is provisioned.
  Calls that only make sense for a provisioned registration (redirect URIs, owners, ID-token issuance, roles, permissions, consent, federation, metadata, `ConfigureInfrastructure`) throw `InvalidOperationException` on an existing application, in either call order. Consumer-side metadata (`WithScope`, `WithApplicationIdUri("api://...")`, `WithFederatedManagedIdentityCredential`) stays allowed.
- `WithReference(entra)` injects configuration as environment variables: Microsoft.Identity.Web names for .NET projects (both `AddDotnetProject` and the obsolete `AddProject<T>`), an uppercase resource-name prefix for other resources.

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

### Running locally

`aspire run` does not provision anything, so `AddPulumiEntraId` resources are hidden from the dashboard and `WithReference(entra)` no longer throws. The registration only exists after a deploy; to sign in locally, supply its values in the AppHost configuration (user secrets or environment variables), and they are injected as `AzureAd__TenantId` / `AzureAd__ClientId`:

```bash
dotnet user-secrets set AzureAd:TenantId <tenant-id>
dotnet user-secrets set AzureAd:ClientId <client-id>
```

The section is named after the connection (`WithReference(entra, "Entra")` reads `Entra:TenantId`). When nothing is configured only `Instance` and the scopes are injected, and `DownstreamApis` entries for provisioned APIs are skipped until they are deployed. Existing registrations (`AddPulumiExistingEntraApplication`) always inject their fixed IDs.

### Notes

- Removing an enabled scope or role is rejected by Graph: set `IsEnabled = false`, deploy, then remove it. Removing `PublishWithAppRoleAssignment` or `WithAdminConsent` does not revoke an existing grant.
- Consent and role assignments need `DelegatedPermissionGrant.ReadWrite.All` / `AppRoleAssignment.ReadWrite.All` and `Application.Read.All` on the deploying principal.
- Federated credentials and role assignments reference the identity in the same Pulumi program.
- Not mirrored: the run-mode local developer certificate and Key Vault certificate credentials; see [decision 0002](https://brianpsheridan.com/Sheridan.Aspire.Pulumi/reference/decisions/0002-entraid-upstream-sync/).
- Customize anything else with `ConfigureInfrastructure(infra => infra.Application.Set(...))`; see the [overlay notes](https://brianpsheridan.com/Sheridan.Aspire.Pulumi/reference/overlays/entraid/).

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.EntraId`](https://github.com/dotnet/aspire/tree/main/src/Aspire.Hosting.Azure.EntraId) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://brianpsheridan.com/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://brianpsheridan.com/Sheridan.Aspire.Pulumi/concepts/naming/).
