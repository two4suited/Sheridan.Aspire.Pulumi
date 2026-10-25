# aspire.pulumi.packages

Aspire hosting integrations that provision Azure infrastructure with Pulumi instead of Bicep/ARM. See [README.md](README.md) for the Pulumi pipeline model (`PulumiStackResource`, `IPulumiProgramContributor`, multi-stack support, `AspirePulumiPackages.Pulumi`/`.Azure`/`.Entra` project split).

## Adding a new resource integration

When adding support for a new Azure resource (a new `AddPulumiX`), mirror the official Aspire integration for that resource rather than inventing the shape from scratch — that's how the ACA environment and the API's deployment to it were built:

1. Find the official Aspire Azure hosting integration if one exists — NuGet package `Aspire.Hosting.Azure.<Name>` (e.g. `Aspire.Hosting.Azure.AppContainers` for `AddAzureContainerAppEnvironment`), source at `github.com/dotnet/aspire/tree/main/src/Aspire.Hosting.Azure.<Name>`.
2. See exactly what it provisions: scaffold a throwaway AppHost that calls the real `AddAzureX(...)`, run `aspire publish`, and read the generated Bicep (or the `Azure.Provisioning` construct source) for the resources, properties, role assignments and outputs it creates.
3. Replicate that shape with Pulumi `azure-native` resources in a new `IPulumiProgramContributor` resource under `AspirePulumiPackages.Pulumi.Azure` — follow `AzureContainerAppEnvironmentResource` as the pattern (resource group, dependent resources, role assignments, `program.Outputs[...]`).
4. Match the official extension-method naming and ergonomics — `AddPulumiX` instead of `AddAzureX`, same `WithX` calls — so swapping Bicep for Pulumi is a drop-in change in `AppHost.cs`.
5. Confirm the exact Pulumi resource tokens and required/output properties with `pulumi package get-schema <provider>` rather than guessing, then verify with `aspire publish` that the generated `Pulumi.yaml` looks right.

When there's no official Aspire/Bicep integration for the resource (e.g. Azure AD app registrations), skip straight to step 5 and work from the Pulumi provider's schema directly.
