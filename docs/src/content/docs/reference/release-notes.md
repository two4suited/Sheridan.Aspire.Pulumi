---
title: Release notes
description: What changed in each Sheridan.Aspire.Pulumi release.
---

Notes for each tagged release, newest first. They are added automatically by the
[Release notes workflow](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/.github/workflows/release-notes.yml)
after a `vX.Y.Z` tag has been published. Packages are on
[nuget.org](https://www.nuget.org/profiles/two4suited).

<!-- releases:start -->

## v2.3.0

_2026-11-15_ · [GitHub release](https://github.com/two4suited/Sheridan.Aspire.Pulumi/releases/tag/v2.3.0)

<!-- Release notes generated using configuration in .github/release.yml at v2.3.0 -->

#### What's Changed
* Fix aspire deploy redeploy: provision step wants to destroy APIM backend/policy by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/231
* ApiManagement: override the backend URL of a compute-targeted backend by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/232


**Full Changelog**: https://github.com/two4suited/Sheridan.Aspire.Pulumi/compare/v2.2.0...v2.3.0


## v2.2.0

_2026-11-15_ · [GitHub release](https://github.com/two4suited/Sheridan.Aspire.Pulumi/releases/tag/v2.2.0)

<!-- Release notes generated using configuration in .github/release.yml at v2.2.0 -->

#### What's Changed
* WithConfig: secret flag and WithProviderConfig shorthand by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/227


**Full Changelog**: https://github.com/two4suited/Sheridan.Aspire.Pulumi/compare/v2.1.0...v2.2.0


## v2.1.0

_2026-11-15_ · [GitHub release](https://github.com/two4suited/Sheridan.Aspire.Pulumi/releases/tag/v2.1.0)

<!-- Release notes generated using configuration in .github/release.yml at v2.1.0 -->

#### What's Changed
* Support local Pulumi backends and plain stack config by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/218
* AppContainers: WithoutAcrPullRoleAssignment for emulators by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/219
* APIM: SystemAssignedIdentity option to avoid failing principalId output on emulators by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/222
* AppContainers: IPulumiPrivateEndpointTarget for the Container Apps environment by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/220
* ApiManagement: networking (VNet integration, private endpoint, publicNetworkAccess) by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/221
* AppContainers: deploy locally built images without pushing (#213) by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/223
* ApiManagement: AddApi<T>(name, path, compute) convenience overload by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/224


**Full Changelog**: https://github.com/two4suited/Sheridan.Aspire.Pulumi/compare/v2.0.0...v2.1.0


## v2.0.0

_2026-11-11_ · [GitHub release](https://github.com/two4suited/Sheridan.Aspire.Pulumi/releases/tag/v2.0.0)

<!-- Release notes generated using configuration in .github/release.yml at v2.0.0 -->

#### What's Changed
* Target .NET 11 by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/208


**Full Changelog**: https://github.com/two4suited/Sheridan.Aspire.Pulumi/compare/v1.6.1...v2.0.0


## v1.6.1

_2026-11-10_ · [GitHub release](https://github.com/two4suited/Sheridan.Aspire.Pulumi/releases/tag/v1.6.1)

<!-- Release notes generated using configuration in .github/release.yml at v1.6.1 -->

#### What's Changed
* Leave app-dependent infrastructure (APIM compute backends) out of the provision pass by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/206
* AppContainers: service-discovery env vars for references to apps in the same environment by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/205


**Full Changelog**: https://github.com/two4suited/Sheridan.Aspire.Pulumi/compare/v1.6.0...v1.6.1


## v1.6.0

_2026-11-09_ · [GitHub release](https://github.com/two4suited/Sheridan.Aspire.Pulumi/releases/tag/v1.6.0)

<!-- Release notes generated using configuration in .github/release.yml at v1.6.0 -->

#### What's Changed
* APIM: OpenAPI import (WithOpenApi) by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/198
* APIM OpenAPI import: review fixes by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/199
* API Management: AsExisting(name, resourceGroup) by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/197
* ApiManagement: AddBackend from a compute resource (container app ingress FQDN) by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/200


**Full Changelog**: https://github.com/two4suited/Sheridan.Aspire.Pulumi/compare/v1.5.2...v1.6.0


## v1.5.2

_2026-11-08_ · [GitHub release](https://github.com/two4suited/Sheridan.Aspire.Pulumi/releases/tag/v1.5.2)

#### What's Changed
* Hide Pulumi-only resources from the dashboard in run mode by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/188


**Full Changelog**: https://github.com/two4suited/Sheridan.Aspire.Pulumi/compare/v1.5.1...v1.5.2


## v1.5.1

_2026-11-08_ · [GitHub release](https://github.com/two4suited/Sheridan.Aspire.Pulumi/releases/tag/v1.5.1)

#### What's Changed
* Cosmos DB: show Data Explorer link in the dashboard by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/184


**Full Changelog**: https://github.com/two4suited/Sheridan.Aspire.Pulumi/compare/v1.5.0...v1.5.1


## v1.5.0

_2026-11-08_ · [GitHub release](https://github.com/two4suited/Sheridan.Aspire.Pulumi/releases/tag/v1.5.0)

#### What's Changed
* Service Bus: emulator parity and dashboard state by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/174
* Storage: emulator parity, container/queue creation, RunAsExisting, Data Lake, dashboard state by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/175
* Service Bus: emulator Config.json uses the fixed namespace name sbemulatorns by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/179

**Full Changelog**: https://github.com/two4suited/Sheridan.Aspire.Pulumi/compare/v1.4.0...v1.5.0


## v1.4.0

_2026-11-08_ · [GitHub release](https://github.com/two4suited/Sheridan.Aspire.Pulumi/releases/tag/v1.4.0)

#### What's Changed
* Service Bus: RunAsEmulator and RunAsExisting for local runs by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/166
* Cosmos DB: RunAsEmulator creates declared databases and containers by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/165


**Full Changelog**: https://github.com/two4suited/Sheridan.Aspire.Pulumi/compare/v1.3.1...v1.4.0


## v1.3.1

_2026-11-08_ · [GitHub release](https://github.com/two4suited/Sheridan.Aspire.Pulumi/releases/tag/v1.3.1)

#### What's Changed
* Front Door: WithAzureFrontDoorPrivateLink for App Service environments by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/159
* ContainerRegistry: WithPurgeTask also purges untagged manifests by @two4suited in https://github.com/two4suited/Sheridan.Aspire.Pulumi/pull/158


**Full Changelog**: https://github.com/two4suited/Sheridan.Aspire.Pulumi/compare/v1.3.0...v1.3.1


## v1.3.0

_2026-11-08_ · [GitHub release](https://github.com/two4suited/Sheridan.Aspire.Pulumi/releases/tag/v1.3.0)

Adds `AsExisting` / `PublishAsExisting` to `AddPulumiServiceBus`, `AddPulumiAzureContainerAppEnvironment` and `AddPulumiAzureContainerRegistry` (#157).

