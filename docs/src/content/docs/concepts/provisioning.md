---
title: "Provisioning and ConfigureInfrastructure"
sidebar:
  order: 4
---

Upstream `Aspire.Hosting.Azure.Provisioning` is a runtime of `BicepValue<T>` factories (literals, functions, member/index
access, operators, conditionals, interpolation, parameters, variables, outputs, user-assigned identities). Bicep needs this
because C# builds an expression tree that is serialized to Bicep text.

## Decision

Pulumi YAML already is the expression language: `${res.prop}` is member access/Output composition, and `fn::join`, `fn::select`,
`fn::split`, `fn::toJSON`, `fn::secret`, `fn::invoke` are the functions, evaluated by the Pulumi engine. We therefore do
**not** port a typed expression tree. `Sheridan.Aspire.Pulumi.Provisioning` provides the pragmatic minimum:

- `PulumiExpr` - builders for references, interpolation, `fn::*` functions, `fn::invoke`, secrets.
- `PulumiResourceBuilder` - construction of a resource (type, properties, options, dependsOn, resource-group placement, tags).
- `PulumiProgramExtensions` - pieces every overlay needs: `AddParameter` (BicepParameter), `AddOutput` (BicepOutput, optionally secret),
  `AddClientConfig`, `AddUserAssignedIdentity`, `AddRoleAssignment`.

## Not ported

- Operators (`+`, `==`, `&&`) and ternaries: Pulumi YAML has none. Resolve conditions and arithmetic in .NET at generation time
  (`PulumiExpr.When`) and emit only the chosen value. Values only known at deploy time cannot be branched on.
- Typed `BicepValue<T>`: values are `object?` YAML nodes; type safety comes from the per-resource overlays built on this project.

Existing integrations (`.Azure`, `.Entra`) can migrate their hand-written `"${...}"` strings to these helpers incrementally.

# ConfigureInfrastructure overlays (issue #38 pattern, reused by #28-#51)

Upstream `Aspire.Hosting.Azure.Provisioning.<X>` overlays let users edit the typed `Azure.Provisioning` resources inside
`ConfigureInfrastructure`. The Pulumi pipeline emits YAML, so azure-native's generated .NET args classes (`Pulumi.AzureNative.*`)
cannot be used. The tradeoff: a **typed-ish property bag**, addressed by azure-native schema names, instead of compile-time types.

## Shared pieces (`Sheridan.Aspire.Pulumi.Provisioning`, `PulumiInfrastructure.cs`)

- `PulumiInfrastructure` - wraps the `PulumiProgram`: `GetResource(id)`, `GetResources(type)`, `GetSingleResource(type)`,
  `AddResource(id, type)`, `AddOutput`. Equivalent of the overlay's identifier lookup.
- `PulumiResourceNode` - editable resource: `Set("a.b.c", value)` (dotted path inside the resource's `properties`, creating maps; null removes),
  `Get`, `Remove`, `SetOption`, `DependsOn`, `Ref(path)`. Values may be literals, `PulumiExpr` results or `${res.prop}` strings
  (that is how `Output<T>` references/expressions are expressed).
- `ConfigureInfrastructure<T>(Action<PulumiInfrastructure>)` on any `IResourceBuilder<T>` stores a `PulumiInfrastructureCallbackAnnotation`.
- `resource.ApplyInfrastructureCallbacks(program)` runs the callbacks (registration order).

## Adding an overlay for resource X (steps)

1. In the hosting resource's `ContributeResourcesAsync`, call `Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program)` as the **last** statement.
2. Add `Pulumi<X>Infrastructure : PulumiInfrastructure` exposing the upstream "lookup roots" as properties returning `PulumiResourceNode`
   (see `PulumiKeyVaultInfrastructure.KeyVaultService`), plus a typed `ConfigureInfrastructure(this IResourceBuilder<Pulumi...Resource>, Action<Pulumi<X>Infrastructure>)`
   extension that wraps the generic one. Guard roots that do not exist (e.g. `PublishAsExisting`).
3. Property paths follow the azure-native schema (`pulumi package get-schema azure-native`): the first segment is the resource's input
   property, e.g. a Vault's settings live under `properties.` (so `Set("properties.enableSoftDelete", true)` emits `properties: { properties: { enableSoftDelete: true } }`).
4. Verify with a throwaway AppHost and check the `aspire publish` output.

Overlay lives in the same project as the hosting port (no separate opt-in package; upstream's `ASPIREAZUREPROVISIONING001` experimental gate is not replicated).

## Key Vault specifics

Root `KeyVaultService` -> `azure-native:keyvault:Vault`; additionally `Secrets`. Unsupported/excluded: polyglot (ATS) projection of types
(no Pulumi equivalent; the callback is C#-only), typed models of `KeyVaultService` sub-types (`KeyVaultAccessPolicy` etc. are plain maps), and
BicepValue operators (see above). Existing vaults (`PublishAsExisting`) have no `KeyVaultService`; other resources remain reachable via `GetResource(id)`.
