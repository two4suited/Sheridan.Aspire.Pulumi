---
title: "0001 - Source generator for Pulumi overlays? (issue #54)"
---

Status: accepted. Decision: **no generator; keep hand-written overlays and add a schema-conformance test instead.**

## Question

Upstream `Aspire.Hosting.Azure.Provisioning.Generators` is a private build-time source generator that emits bounded
C# proxies over explicit Azure SDK type selections (`AtsTypeMappings.cs`). Should we build the Pulumi equivalent, for example
generating typed customization overlays from `pulumi package get-schema azure-native` for the tier-5 overlay issues
(#28-#51), or keep writing overlays by hand?

## What exists today (main)

- `Sheridan.Aspire.Pulumi.Provisioning` is about 170 lines in total: `PulumiExpr` (71), `PulumiResourceBuilder` (53),
  `PulumiProgramExtensions` (44). Per [the provisioning guide](/Sheridan.Aspire.Pulumi/concepts/provisioning/), Pulumi YAML is already the expression language, so there is no
  typed `BicepValue<T>` layer to wrap.
- Tier-1 ports (hand-written, each mirroring the official Bicep output): OperationalInsights ~60 lines, ContainerRegistry
  ~215, KeyVault ~300, Storage ~305, Network ~630 (the outlier: many resource kinds plus types). Median about 300 lines, most
  of which is Aspire surface (annotations, `WithX` extensions, role enums, emulator wiring), not property mapping.
- A typical resource body is a nested `Dictionary<string, object?>` with `type` + `properties` (see
  `AzureContainerRegistryResource`): roughly 10-30 lines per azure-native resource.

## Scale of the work a generator would target

- Tier-5 overlay issues: **24** (#28-#51, one per upstream `...Provisioning.<X>` package; #38 KeyVault is labelled tier-2
  but is the same kind of work). Each pairs with a larger tier 1-3 port that needs the same resources.
- Realistic overlay content per issue: a handful of azure-native resources, 10-30 lines each, so about 50-250 lines, or
  about 3-5k lines across all 24 if hand-written.
- The schema is huge: `azure-native` 3.29.0 has 22,969 resources, 220,654 types and 28,297 functions (526 MB of JSON).
  `keyvault:Vault` has 5 inputs; `containerregistry:Registry` has 15. We use only a few percent of any resource's surface,
  and only the properties that the upstream Bicep sets.

## Why a generator does not pay off

1. **Nothing to generate that adds safety.** Upstream's generator exists to give C# typed access to SDK models. Our
   overlays emit untyped YAML nodes by design (the provisioning guide). A generated typed wrapper would be a new abstraction to
   keep in sync with YAML emission, wrapping about 10 properties per resource.
2. **The valuable part is judgement, not mapping.** Each port reproduces the official integration's chosen defaults (SKU,
   TLS, `disableLocalAuth`, tags, role assignments) read from `aspire publish` Bicep. A schema cannot say which properties to
   set or to what.
3. **Cost vs. savings.** A generator (schema loader that copes with a 526 MB document and version drift, selection file,
   emitter, incremental-generator plumbing, snapshot tests) is weeks of work plus permanent maintenance, to save a few hundred
   lines per port that are cheap to write and review. Break-even needs hundreds of resource shapes; we have 24 overlays and
   perhaps 60-80 distinct resource types.
4. **Build risk.** A build-time generator needing the schema forces either a huge checked-in snapshot or a dependency on the
   `pulumi` CLI and network at build time.

## Cheaper alternative (adopted)

The real risk with hand-written dictionaries is typos in resource tokens and property names, which only fail at `pulumi up`.
Address that directly:

- **Schema-conformance test**: run `pulumi package get-schema azure-native` once (cached, or a trimmed snapshot of just the
  tokens we use) and check every `type` token and property key emitted by each overlay against the schema's `inputProperties`
  (and every `${res.prop}` / output name against `properties`). This catches the class of bug a generator would, in one test file.
- Keep using `PulumiResourceBuilder` / `PulumiProgramExtensions`; add small helpers only when three or more ports repeat the
  same shape (role assignments, diagnostic settings, private endpoints).
- Continue the per-port workflow in CLAUDE.md (step 5: confirm tokens with `pulumi package get-schema`).

## Revisit when

More than ~100 distinct resource shapes are ported, or a consumer needs to author arbitrary azure-native resources from C#
(a general-purpose typed API). A generator limited to an explicit selection list (as upstream does) then becomes reasonable,
and the conformance test's schema snapshot would be its starting input.

## Follow-ups

- One issue: add the schema-conformance test (milestone "Convert Aspire Bicep packages to Pulumi components").
- No generator issues.

## Status of the conformance test

The schema-conformance test project (which ran `aspire publish` against a demo AppHost and validated the generated `Pulumi.yaml`
against `pulumi package get-schema azure-native`) was removed together with the demo AppHost when the repository was reduced to the
packages. Token and property validity is now checked by unit tests and by verifying `aspire publish` output in a throwaway AppHost.
