# Sheridan.Aspire.Pulumi

Aspire hosting integrations that provision Azure infrastructure with Pulumi instead of Bicep/ARM. See [README.md](README.md) for the Pulumi pipeline model (`PulumiStackResource`, `IPulumiProgramContributor`, multi-stack support, `Sheridan.Aspire.Pulumi`/`.Azure`/`.Entra` project split).

## Adding a new resource integration

When adding support for a new Azure resource (a new `AddPulumiX`), mirror the official Aspire integration for that resource rather than inventing the shape from scratch — that's how the ACA environment and the API's deployment to it were built:

1. Find the official Aspire Azure hosting integration if one exists — NuGet package `Aspire.Hosting.Azure.<Name>` (e.g. `Aspire.Hosting.Azure.AppContainers` for `AddAzureContainerAppEnvironment`), source at `github.com/dotnet/aspire/tree/main/src/Aspire.Hosting.Azure.<Name>`.
2. See exactly what it provisions: scaffold a throwaway AppHost that calls the real `AddAzureX(...)`, run `aspire publish`, and read the generated Bicep (or the `Azure.Provisioning` construct source) for the resources, properties, role assignments and outputs it creates.
3. Replicate that shape with Pulumi `azure-native` resources in a new `IPulumiProgramContributor` resource under `Sheridan.Aspire.Pulumi.Azure` — follow `AzureContainerAppEnvironmentResource` as the pattern (resource group, dependent resources, role assignments, `program.Outputs[...]`).
4. Match the official extension-method naming and ergonomics — `AddPulumiX` instead of `AddAzureX`, same `WithX` calls — so swapping Bicep for Pulumi is a drop-in change in `AppHost.cs`.
5. Confirm the exact Pulumi resource tokens and required/output properties with `pulumi package get-schema <provider>` rather than guessing, then verify with `aspire publish` that the generated `Pulumi.yaml` looks right.

When there's no official Aspire/Bicep integration for the resource (e.g. Azure AD app registrations), skip straight to step 5 and work from the Pulumi provider's schema directly.

### New package checklist

Every new project under `src/` must also be wired into the docs and the release pipeline:

- **Root README**: add a row to the package table in [README.md](README.md) (`## Packages`) in the right area, linking to `src/<Package>` with a one-line "Provisions" description. Update the install/usage examples or pipeline notes there if the new package changes them.
- **Docs site**: [docs/](docs) is an Astro Starlight site. The Integrations pages are generated from the root README package table and each `src/<Package>/README.md` (`docs/scripts/sync-packages.mjs`), so the table row and package README above are all a new package needs. Hand-written pages live under `docs/src/content/docs/` (concepts, get-started, reference/overlays, reference/decisions); run `npm run build` in `docs/` to check it.
- **Package README**: create `src/<Package>/README.md` (it's packed into the NuGet package; without it the root README is used instead).
- **Published**: packages are published by `.github/workflows/publish-packages.yml`, which packs every `src/*/*.csproj` when a `vX.Y.Z` tag is pushed and publishes to nuget.org (and GitHub Packages). Keep the project under `src/` (`src/Directory.Build.props` sets `IsPackable`, `PackageId` = project name), don't set `IsPackable` to false, and confirm it appears via `dotnet pack src/<Package> -c Release -o <tmp>`. Make sure it's included in `Sheridan.Aspire.Pulumi.slnx`.
- **Description**: give the csproj a short `<Description>` (2–3 sentences: what it provisions, the `AddPulumiX` entry point, and a link to the package README). Keep it brief; GitHub renders it as one plain-text block. nuget.org renders each package's embedded README (and shows the description); GitHub Packages doesn't (it shows the repo README on the package page and only the description on the version page).
- After a release tag, verify the new package shows up in the workflow's `ls -1 artifacts` output and on nuget.org (`https://www.nuget.org/packages/<PackageId>`; new packages can take up to an hour to validate and index). nuget.org publishing uses trusted publishing (OIDC) via the `NUGET_USER` repo variable; no API key is stored.

### Release notes

After a release tag is published, `.github/workflows/release-notes.yml` (triggered by the "Publish packages" workflow completing) copies the GitHub Release body into `docs/src/content/docs/reference/release-notes.md` via `docs/scripts/add-release-notes.mjs` and opens a `release-notes/<tag>` PR against `main`; merging it redeploys the docs. It needs "Allow GitHub Actions to create and approve pull requests" enabled in repo settings. Backfill an older tag with the workflow's manual `tag` input. Don't edit the `<!-- releases:start -->` marker in that page.

## Working on issues

When asked to work on a GitHub issue:

1. Branch off `main` (never commit to `main` directly) and implement the change, including tests under `tests/` where the affected project has them.
2. Update the README of every package you touched (`src/<Package>/README.md`) in the same change: document new or changed `AddPulumiX` / `WithX` APIs, options, outputs and any behavior or breaking changes, with a usage snippet that matches the real extension-method signatures. For a brand-new package, create its README following the existing package READMEs as the template. If the change affects the pipeline model or package list, update the root [README.md](README.md) too.
3. Build and run the relevant tests before opening the PR (`dotnet build` / `dotnet test`).
4. Open a pull request with `gh pr create` that references the issue (`Closes #<n>`), summarizes what changed, and notes the README updates. Don't merge it yourself.
