# Sheridan.Aspire.Pulumi.Entra

Azure AD (Entra) app registrations provisioned by Pulumi through the `azuread` provider.

`AddPulumiAzureAdAppRegistration` provisions an `azuread:Application` and an `azuread:ServicePrincipal` in the stack it is bound to. Its outputs can feed other resources, such as the client id of your API.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Entra
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var entraStack = builder.AddPulumiStack("my-app-entra", "dev")
    .WithProjectName("my-app")
    .WithOrganization("my-org")
    .WithEscEnvironment("entra/dev");   // must supply the azuread provider credentials

var appRegistration = builder.AddPulumiAzureAdAppRegistration("app-registration")
    .WithPulumiStack(entraStack)
    .WithDisplayName("My API");

builder.AddProject<Projects.MyApi>("api")
    .WithEnvironment("AzureAd__ClientId", appRegistration.Resource.ClientId);

builder.Build().Run();
```

## Features

- `ClientId`, `ObjectId` and `ServicePrincipalObjectId` are exposed as `PulumiOutputReference`s.
- `WithDisplayName(...)` overrides the display name (defaults to the resource name).
- Because the API references the registration, the main stack's provision step is ordered after the Entra stack's deploy step automatically, and the client id resolves on the first `aspire deploy`.

## Notes

- The ESC environment bound to the stack must supply the `azuread` provider credentials (tenant, client id/secret or OIDC).
- Outputs are read with `pulumi stack output`, not resolved as a live Pulumi cross-stack reference.
- This is the lower-level option. For the Aspire-style API (`AddPulumiEntraId`, with `WithReference`, scopes, app roles and consent), prefer `Sheridan.Aspire.Pulumi.Azure.EntraId`.

## Learn more

- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
