# Pulumi provisioning foundation (issue #53)

Upstream `Aspire.Hosting.Azure.Provisioning` is a runtime of `BicepValue<T>` factories (literals, functions, member/index
access, operators, conditionals, interpolation, parameters, variables, outputs, user-assigned identities). Bicep needs this
because C# builds an expression tree that is serialized to Bicep text.

## Decision

Pulumi YAML already is the expression language: `${res.prop}` is member access/Output composition, and `fn::join`, `fn::select`,
`fn::split`, `fn::toJSON`, `fn::secret`, `fn::invoke` are the functions, evaluated by the Pulumi engine. We therefore do
**not** port a typed expression tree. `AspirePulumiPackages.Pulumi.Provisioning` provides the pragmatic minimum:

- `PulumiExpr` - builders for references, interpolation, `fn::*` functions, `fn::invoke`, secrets.
- `PulumiResourceBuilder` - construction of a resource (type, properties, options, dependsOn, resource-group placement, tags).
- `PulumiProgramExtensions` - pieces every overlay needs: `AddParameter` (BicepParameter), `AddOutput` (BicepOutput, optionally secret),
  `AddClientConfig`, `AddUserAssignedIdentity`, `AddRoleAssignment`.

## Not ported

- Operators (`+`, `==`, `&&`) and ternaries: Pulumi YAML has none. Resolve conditions and arithmetic in .NET at generation time
  (`PulumiExpr.When`) and emit only the chosen value. Values only known at deploy time cannot be branched on.
- Typed `BicepValue<T>`: values are `object?` YAML nodes; type safety comes from the per-resource overlays built on this project.

Existing integrations (`.Azure`, `.Entra`) can migrate their hand-written `"${...}"` strings to these helpers incrementally.
