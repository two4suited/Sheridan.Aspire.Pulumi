---
description: Weekly review of upstream microsoft/aspire hosting integrations against this repo; files one issue per integration that needs changes.
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
timeout-minutes: 30
network: defaults
tools:
  github:
    toolsets: [repos, issues]
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
(`Aspire.Hosting.Azure.<Name>`). Each local project `src/AspirePulumiPackages.Pulumi.Azure.<Name>`
mirrors the upstream `src/Aspire.Hosting.Azure.<Name>` project (extension-method names, `WithX`
ergonomics, provisioned resources, role assignments, outputs). Your job is to detect upstream
changes that this repo should react to and file actionable issues.

## Step 1: Learn this repo

Read `CLAUDE.md`, `README.md`, `docs/naming.md`, `docs/provisioning.md` and `docs/decisions/`
(skip anything missing). Then list `src/` and build a mapping
`local project -> upstream project`. Also note the pinned `Aspire.Hosting` version in the
`.csproj` files. Do not file issues for things `docs/decisions/` records as intentionally
different or out of scope.

## Step 2: Find what changed upstream

Read `/tmp/gh-aw/cache-memory/last-reviewed.json` if it exists (`{"sha": "...", "date": "..."}`).
Use `workflow_dispatch` input `since` (`${{ github.event.inputs.since }}`) when given; otherwise use
the stored SHA; on the very first run use commits from the last 30 days.

Using the GitHub tools against `microsoft/aspire` (default branch `main`), list commits to `src/`
since that point. For each commit touching a mapped project (`src/Aspire.Hosting.Azure.<Name>` and
the matching `src/Aspire.Hosting.Azure.Provisioning.<Name>` where present), fetch the diff and
understand it. Ignore pure refactors, test-only, docs-only, analyzer, dependency-bump and
formatting changes unless they alter public API or the provisioned resource shape.

Also compare the *set* of upstream `Aspire.Hosting.Azure.*` projects with `src/`:
- A new upstream integration with no local project is a "missing integration" finding.
- A removed or renamed upstream integration is a "rename/removal" finding.

Also check the latest `microsoft/aspire` release; if it is newer than the pinned `Aspire.Hosting`
version, include a "version bump" note in any issue you file (and file a standalone bump issue only
if nothing else is being filed).

## Step 3: Decide what is actionable

A change is actionable if it affects a local project in one of these ways:
- New or changed public `AddAzureX` / `WithX` extension methods, parameters, or defaults
  (needs a matching `AddPulumiX` / `WithX`; names follow `docs/naming.md`).
- New or changed Azure resources, properties, SKUs, API versions, networking/private-endpoint
  behaviour, or diagnostic settings in what the Bicep/`Azure.Provisioning` code generates.
- New or changed role assignments / built-in role definitions, managed identity wiring.
- New or changed outputs, connection-string formats, or environment variables.
- Bug fixes in provisioning logic that apply equally to the Pulumi implementation.

Verify against the local code before deciding: open the local project and check whether it
already handles the change. Do not file for things that are already implemented.

## Step 4: Avoid duplicates

Search existing issues (open and closed) in this repo, with the `upstream-sync` label or
`[aspire-sync]` title prefix, for the same integration and the same upstream commits/PRs. Skip
anything already covered. If an open issue exists for the integration but misses new upstream
changes, do not file a second one; mention it in the run summary only.

## Step 5: File issues

Create **one issue per local integration** that has actionable changes (plus one per missing
upstream integration), at most 10 per run; if there are more, file the most impactful and list
the rest in the final summary. Issue format:

- Title: `<Name>: <short summary>` (the `[aspire-sync]` prefix is added automatically).
- Body sections:
  1. **Upstream changes** - bullet list, each with a link to the upstream commit/PR and a
     one-line description.
  2. **Impact on this repo** - which local files/classes are affected
     (e.g. `src/AspirePulumiPackages.Pulumi.Azure.KeyVault/AzureKeyVaultResourceExtensions.cs`) and
     what is currently different.
  3. **Suggested change** - concrete steps, following the pattern in `CLAUDE.md`
     ("Adding a new resource integration"): confirm Pulumi `azure-native` tokens and properties with
     `pulumi package get-schema azure-native`, mirror upstream naming, verify with `aspire publish`.
  4. **Priority** - `breaking`, `feature`, or `minor`, with a sentence of justification.

Be precise and concise. Never invent upstream changes; every claim must link to a commit or file.

## Step 6: Record progress

After filing, write `/tmp/gh-aw/cache-memory/last-reviewed.json` with the newest upstream commit SHA
you reviewed (even if you filed nothing) and today's date, so the next run starts from there.
If you found nothing actionable, call the `noop` safe output with a brief summary of what was
reviewed.
