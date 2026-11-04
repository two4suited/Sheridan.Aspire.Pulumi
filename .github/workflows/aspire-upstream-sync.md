---
description: Weekly review of upstream microsoft/aspire hosting integrations against this repo; files one issue per integration that needs changes, with concrete resolution guidance.
on:
  schedule: weekly
  workflow_dispatch:
    inputs:
      since:
        description: "Override: review upstream commits since this date or SHA (default: last reviewed SHA)"
        required: false
permissions:
  contents: read
  issues: read
engine:
  id: claude
  model: claude-sonnet-5-5
timeout-minutes: 45
network: defaults
tools:
  github:
    toolsets: [repos, issues, pull_requests]
  cache-memory: true
  bash: ["git", "grep", "ls", "cat", "find", "head", "wc"]
safe-outputs:
  create-issue:
    title-prefix: "[aspire-sync] "
    labels: [upstream-sync, automation]
    max: 10
---

# Aspire upstream sync

This repository (`${{ github.repository }}`) provides Pulumi-based replacements for the official
Aspire Azure hosting integrations in <https://github.com/microsoft/aspire/tree/main/src>
(`Aspire.Hosting.Azure.<Name>`). Each local project `src/Sheridan.Aspire.Pulumi.Azure.<Name>`
mirrors the upstream `src/Aspire.Hosting.Azure.<Name>` project (extension-method names, `WithX`
ergonomics, provisioned resources, role assignments, outputs). Your job is to detect upstream
changes that this repo should react to and file issues that a contributor can pick up and finish
without redoing your research.

Always discover real paths with `ls`/`git grep` instead of assuming them. Project and namespace
names have changed in this repo before.

## Step 1: Learn this repo

Read `CLAUDE.md`, `README.md`, `docs/naming.md`, `docs/provisioning.md`, `docs/decisions/` and the
matching `docs/overlays/<Name>.md` files (skip anything missing). Then list `src/` and `tests/` and
build a mapping `local project -> upstream project`. Note the pinned `Aspire.Hosting` version in the
`.csproj` files. Do not file issues for things `docs/decisions/` records as intentionally different
or out of scope.

Remember these repo conventions, because the guidance you write must follow them:
- Names are built by the naming layer (`AzureNaming`, `AzureNameKind`, `NameFor(...)`); integrations
  never use Pulumi autonaming or invent names.
- Resource bodies are hand-written `Dictionary<string, object?>` nodes (`type` + `properties`) built
  with `PulumiResourceBuilder` / `PulumiProgramExtensions`; there is no generator.
- Upstream's `ConfigureInfrastructure` customisation is mirrored by a `Pulumi<Name>Infrastructure`
  type documented in `docs/overlays/<Name>.md`.
- Existing-resource support follows the `PublishAsExisting` / `AsExisting` pattern that other
  integrations already implement (find a current example with `git grep -n PublishAsExisting src`).

## Step 2: Find what changed upstream

Read `/tmp/gh-aw/cache-memory/last-reviewed.json` if it exists (`{"sha": "...", "date": "..."}`).
Use the `workflow_dispatch` input `since` (`${{ github.event.inputs.since }}`) when given; otherwise
use the stored SHA; on the very first run use commits from the last 30 days.

Using the GitHub tools against `microsoft/aspire` (default branch `main`), list commits to `src/`
since that point. For each commit touching a mapped project (`src/Aspire.Hosting.Azure.<Name>` and
the matching `src/Aspire.Hosting.Azure.Provisioning.<Name>` where present), find the pull request it
came from and **read the PR description and the changed source files, not just the title**.
Understand the public API and what gets provisioned. Ignore pure refactors, test-only, docs-only,
analyzer, dependency-bump and formatting changes unless they alter public API or the provisioned
resource shape.

Also compare the *set* of upstream `Aspire.Hosting.Azure.*` projects with `src/`:
- A new upstream integration with no local project is a "missing integration" finding.
- A removed or renamed upstream integration is a "rename/removal" finding.

For every PR you intend to cite, determine whether it is in the **latest released** version of
`microsoft/aspire` and in the version this repo pins (for example by comparing the PR merge commit
against the release tag). Record this as `released in vX`, `on main only`, or `unknown`.

## Step 3: Decide what is actionable

A change is actionable if it affects a local project in one of these ways:
- New or changed public `AddAzureX` / `WithX` / `AsExisting` extension methods, parameters or defaults
  (needs a matching `AddPulumiX` / `WithX`; names follow `docs/naming.md`).
- New or changed Azure resources, properties, SKUs, API versions, networking/private-endpoint
  behaviour, or diagnostic settings in what the Bicep/`Azure.Provisioning` code generates.
- New or changed role assignments / built-in role definitions, managed identity wiring.
- New or changed outputs, connection-string formats, or environment variables.
- Bug fixes in provisioning logic that apply equally to the Pulumi implementation.

Verify against the local code before deciding: open the local project and check whether it already
handles the change. Do not file for things that are already implemented. Never say you only checked a
PR title: if you could not read a diff, say exactly what you could not read and mark the claim
unverified.

## Step 4: Avoid duplicates

Search existing issues (open and closed) in this repo, with the `upstream-sync` label or
`[aspire-sync]` title prefix, for the same integration and the same upstream PRs. Skip anything
already covered. If an open issue exists for the integration but misses new upstream changes, do not
file a second one; mention it in the run summary only.

## Step 5: File issues

Create **one issue per local integration** that has actionable changes (plus one per missing
upstream integration), at most 10 per run; if there are more, file the most impactful and list the
rest in the final summary. Write each issue so that someone who has never seen the upstream PR can
implement it.

Title: `<Name>: <short summary>` (the `[aspire-sync]` prefix is added automatically).

Body sections, in this order:

1. **Upstream changes** - one bullet per PR: link, one-line description, and its status
   (`released in vX` / `on main only` / `unknown`).

2. **Upstream API** - the new or changed public surface, copied accurately from the upstream source:
   method signatures and a short usage example (from the PR description or playground app). Link the
   source files. This is what we must mirror.

3. **What upstream provisions** - the resulting resource/property/output changes (for example
   `publicNetworkAccess: Disabled`, SKU switches, new outputs, new role assignments), taken from the
   source or the generated Bicep. Name the Bicep/`Azure.Provisioning` types involved.

4. **Impact on this repo** - verified findings only: the exact local files and types affected
   (real paths you listed), what exists today, and what is missing. Mark anything unverified.

5. **How to resolve** - a concrete plan for a contributor:
   - **Files to change:** the local files/classes to edit or add, including the extension class, the
     resource class, the `Pulumi<Name>Infrastructure` overlay type if relevant, the package `README.md`
     and the matching `docs/overlays/<Name>.md`.
   - **Pattern to copy:** link the closest existing local implementation of the same kind of feature
     (for example the local `PublishAsExisting` implementation for existing-resource support), so the
     contributor copies a working shape instead of inventing one.
   - **Pulumi mapping:** the `azure-native` (or `azuread` for Entra) resource tokens and property
     names that correspond to the upstream Bicep, confirmed with
     `pulumi package get-schema <provider>` where the CLI is available; otherwise state they are
     unconfirmed.
   - **Repo conventions to follow:** naming through `NameFor`/`AzureNameKind`, no autonaming, YAML
     node style, and any decision in `docs/decisions/` that applies.
   - **Gotchas:** behaviour that differs between Bicep and Pulumi, ordering/dependency concerns,
     validation upstream performs, and anything the PR description warns about.
   - **Task list:** a markdown checklist (`- [ ]`) of small steps. If the work is large, split it into
     independently mergeable chunks, typically one per upstream PR.

6. **How to verify** - a checklist: `dotnet build` and tests under `tests/` (name the existing test
   project to extend, and the cases to add), regenerate the stack with `aspire publish` and compare
   the resulting `Pulumi.yaml` against the Bicep from a throwaway AppHost using the official
   `AddAzureX`, and run the schema-conformance check if it exists.

7. **Priority and sizing** - `breaking`, `feature` or `minor` with a sentence of justification, plus
   a rough size (`S`, `M`, `L`). If the upstream change is `on main only` or marked experimental,
   say so and recommend whether to wait for a release.

Be precise. Never invent upstream changes, signatures or file paths: every claim must trace to a PR,
commit or file you actually read, and unverified claims must be labelled as such.

## Step 6: Record progress

After filing, write `/tmp/gh-aw/cache-memory/last-reviewed.json` with the newest upstream commit SHA
you reviewed (even if you filed nothing) and today's date, so the next run starts from there.
If you found nothing actionable, call the `noop` safe output with a brief summary of what was
reviewed.
