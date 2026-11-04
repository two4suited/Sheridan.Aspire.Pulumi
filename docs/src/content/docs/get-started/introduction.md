---
title: Introduction
description: What Sheridan.Aspire.Pulumi is and how its packages fit together.
---

Sheridan.Aspire.Pulumi is a family of Aspire hosting integrations that provision Azure infrastructure with [Pulumi](https://www.pulumi.com) instead of Bicep/ARM. Each package mirrors the official `Aspire.Hosting.Azure.*` integration (`AddPulumiX` instead of `AddAzureX`, same `WithX` calls), so swapping Bicep for Pulumi is a drop-in change in your AppHost.

## How the packages fit together

- **Core**: [`Sheridan.Aspire.Pulumi`](/Sheridan.Aspire.Pulumi/integrations/core/sheridan-aspire-pulumi/) provides `AddPulumiStack`, program/YAML generation and the Pulumi CLI pipeline. Every other package builds on it.
- **Shared Azure layers**: `Provisioning`, `Azure` and `Azure.Base` supply `ConfigureInfrastructure`, shared interfaces, resource groups, identities, role definitions and naming.
- **One package per Azure service**: compute, data, messaging, AI, config and security, networking and monitoring. Install only the ones you use. See [Integrations](/Sheridan.Aspire.Pulumi/integrations/).

The package id equals the folder name under `src/`, and namespaces match the package ids.

## What you need

- The [Aspire CLI](https://aspire.dev)
- The Pulumi CLI, signed in with `pulumi login`
- An Azure subscription
