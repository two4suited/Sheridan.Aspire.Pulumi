---
title: Release notes
description: What changed in each Sheridan.Aspire.Pulumi release.
---

Notes for each tagged release, newest first. They are added automatically by the
[Release notes workflow](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/.github/workflows/release-notes.yml)
after a `vX.Y.Z` tag has been published. Packages are on
[nuget.org](https://www.nuget.org/profiles/two4suited).

<!-- releases:start -->

## v1.5.0

_2026-11-08_ · [GitHub release](https://github.com/two4suited/Sheridan.Aspire.Pulumi/releases/tag/v1.5.0)

#### What's Changed
* Docs: release notes for v1.4.0 by @github-actions[bot] in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/167
* Service Bus: emulator parity and dashboard state by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/174
* Storage: emulator parity, container/queue creation, RunAsExisting, Data Lake, dashboard state by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/175
* Service Bus: emulator Config.json uses the fixed namespace name sbemulatorns by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/179
* CLAUDE.md: release by pushing a tag only by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/180

#### New Contributors
* @github-actions[bot] made their first contribution in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/167

**Full Changelog**: https://github.com/two4suited/Sheridan.Aspire.Pulumi/compare/v1.4.0...v1.5.0


## v1.4.0

_2026-11-08_ · [GitHub release](https://github.com/two4suited/Sheridan.Aspire.Pulumi/releases/tag/v1.4.0)

#### What's Changed
* Add release notes workflow and docs page by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/161
* Docs: link release notes from landing page and README by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/164
* Service Bus: RunAsEmulator and RunAsExisting for local runs by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/166
* Cosmos DB: RunAsEmulator creates declared databases and containers by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/165


**Full Changelog**: https://github.com/two4suited/Sheridan.Aspire.Pulumi/compare/v1.3.1...v1.4.0


## v1.3.1

_2026-11-08_ · [GitHub release](https://github.com/two4suited/Sheridan.Aspire.Pulumi/releases/tag/v1.3.1)

#### What's Changed
* Front Door: WithAzureFrontDoorPrivateLink for App Service environments by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/159
* CLAUDE.md: require an Opus subagent review of issue PRs by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/160
* ContainerRegistry: WithPurgeTask also purges untagged manifests by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/158


**Full Changelog**: https://github.com/two4suited/Sheridan.Aspire.Pulumi/compare/v1.3.0...v1.3.1


## v1.3.0

_2026-11-08_ · [GitHub release](https://github.com/two4suited/Sheridan.Aspire.Pulumi/releases/tag/v1.3.0)

Adds `AsExisting` / `PublishAsExisting` to `AddPulumiServiceBus`, `AddPulumiAzureContainerAppEnvironment` and `AddPulumiAzureContainerRegistry` (#157).

