# Sheridan.Aspire.Pulumi.Entra

_Azure AD app registration_ — part of [Sheridan.Aspire.Pulumi](../../README.md).

`builder.AddPulumiAzureAdAppRegistration("app-registration")` provisions an `azuread:Application` plus a `azuread:ServicePrincipal` (via the `azuread` Pulumi provider) in whichever stack it's bound to with `.WithPulumiStack(...)`. Use `.WithDisplayName(...)` to override the display name (defaults to the resource name). The ESC environment bound to that stack must supply the `azuread` provider's credentials (tenant, client id/secret or OIDC), the same way `azure/test` supplies `azure-native`'s.

The resource exposes `ClientId`, `ObjectId` and `ServicePrincipalObjectId` as `PulumiOutputReference`s, so they can be fed into another resource's config the same way any Aspire reference is:

```csharp
var appRegistration = builder.AddPulumiAzureAdAppRegistration("app-registration")
    .WithPulumiStack(entraStack);

builder.AddProject<Projects.MyApi>("api")
    .WithComputeEnvironment(acaEnv)
    .WithEnvironment("AzureAd__ClientId", appRegistration.Resource.ClientId);
```

These outputs are read with `pulumi stack output` from the `entra-app` stack, not resolved as a live Pulumi cross-stack reference. Because the API references `appRegistration`, the main stack's provision step is ordered after `entra-app`'s deploy step automatically (no `DependsOnStack`/`WaitFor` needed, though both still work), and the main program is regenerated after that, so the client id resolves on the first `aspire deploy`.
