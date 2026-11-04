---
title: "0002 - Entra ID: mirroring the upstream scopes, roles, federation and consent APIs (issue #144)"
---

Status: accepted. This is a breaking change of the `Sheridan.Aspire.Pulumi.Azure.EntraId` contract (see "Breaking changes").

## Implemented

Application roles (`WithAppRole`), identifier URI / scopes / pre-authorization (`WithApplicationIdUri`, `WithScope`,
`WithPreAuthorizedApplication`), caller permissions and consent (`WithApiPermissions`, `WithAdminConsent`), federated managed identity
credentials (`PublishWithFederatedManagedIdentityCredential`, `WithFederatedManagedIdentityCredential`), app-role assignments for managed
identities (`PublishWithAppRoleAssignment`), existing-reference guards, consumer configuration in `WithReference`, and the
`ConfigureInfrastructure` overlay ([overlay doc](/Sheridan.Aspire.Pulumi/reference/overlays/entraid/)).

## Decisions

- **Local developer certificate (upstream #21382): not implemented.** It is a run-mode concept (a 48-hour certificate in the Aspire
  store). Pulumi stacks have no run mode, and a short-lived locally generated secret does not belong in a Pulumi state.
- **Key Vault certificate credentials (upstream #21425): not implemented.** `azuread` `ApplicationCertificate` takes the certificate
  value, not a Key Vault URI, and upstream's automatic Key Vault Certificate User role grant has no equivalent here yet. Follow-up.
- **Default identifier URI** (`api://{clientId}`): the client ID only exists after the application, so it is an
  `ApplicationIdentifierUri` resource and the application ignores changes to `identifierUris`. Upstream's duplicate-declaration PATCH
  workaround is not needed and not copied.
- **Stable IDs**: scope and role GUIDs are derived (SHA-256, name based) from the registration name and the scope name / role value.
- **Pre-authorization cycles**: `ApplicationPreAuthorized` is its own resource, so two registrations may authorize each other; upstream's
  restriction does not apply.
- **Managed identities**: federated credentials, role assignments and `PrincipalIdExpression` are in-program references, so the identity
  and the registration must be provisioned by the same Pulumi program. App-role assignments take an `IPulumiManagedIdentityResource`;
  compute resources are not accepted directly. The workload's `ManagedIdentityClientId` is only injected for the config-only
  `WithFederatedManagedIdentityCredential(clientId)`.
- **Retiring scopes and roles**: Graph rejects deleting an enabled scope or role. Set `IsEnabled = false`, deploy, then remove it.
- **Permissions of the deploying principal**: admin consent needs `DelegatedPermissionGrant.ReadWrite.All` /
  `AppRoleAssignment.ReadWrite.All`, role assignments `AppRoleAssignment.ReadWrite.All`, both plus `Application.Read.All`.

## Breaking changes

- `WithRedirectUris`, `WithApplicationOwners`, `WithServicePrincipalOwners`, `WithOwners` and `WithIdTokenIssuance` now throw
  `InvalidOperationException` on existing applications (they used to do nothing), and `AsExistingApplication` throws if such settings were applied first.
- `WithReference` now takes optional connection / downstream-API key names and injects more variables for .NET projects.
- The Entra project now references `Sheridan.Aspire.Pulumi.Provisioning` and `Sheridan.Aspire.Pulumi.Azure`.
- The schema property is `requiredResourceAccesses` (the issue text had `requiredResourceAccess`).

## Not verified

Everything is covered by unit tests of the emitted program; no `pulumi preview`/`up` against a tenant or comparison with upstream `aspire publish` output was run.
