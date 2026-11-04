---
title: "EntraId overlay (issue #144)"
---

Pulumi counterpart of the public Graph provisioning models upstream (`MicrosoftGraphApplication`,
`MicrosoftGraphServicePrincipal`, `MicrosoftGraphFederatedIdentityCredential`; upstream PR #21724). The overlay lives alongside the
hosting project (`Sheridan.Aspire.Pulumi.Azure.EntraId`). See [provisioning.md](/Sheridan.Aspire.Pulumi/concepts/provisioning/) for the shared
`ConfigureInfrastructure` machinery.

## Roots

| Upstream model | Pulumi property | Resource |
| --- | --- | --- |
| `MicrosoftGraphApplication` | `infra.Application` | `azuread:index/application:Application` |
| `MicrosoftGraphServicePrincipal` | `infra.ServicePrincipal` | `azuread:index/servicePrincipal:ServicePrincipal` |
| `MicrosoftGraphFederatedIdentityCredential` | `infra.FederatedIdentityCredentials` | `azuread:index/applicationFederatedIdentityCredential:...` |

`ConfigureInfrastructure` throws for existing applications (nothing is provisioned). The callback runs last.
Property paths are `azuread` input names (verified with `pulumi package get-schema azuread`).

```csharp
builder.AddPulumiEntraId("entra")
    .ConfigureInfrastructure(infra =>
    {
        infra.Application.Set("groupMembershipClaims", new List<object?> { "SecurityGroup" });
        infra.ServicePrincipal.Set("accountEnabled", true);
    });
```

Common properties also have dedicated calls: `WithTags`, `WithServiceManagementReference`, `WithDescription`.
