# Sheridan.Aspire.Pulumi.Azure.Network

Virtual networks, subnets, NAT, private endpoints, private DNS and network security perimeters provisioned by Pulumi.

The Pulumi counterpart of `Aspire.Hosting.Azure.Network`. Build a virtual network, delegate subnets to compute environments, and put PaaS resources behind private endpoints.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.Network
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var vnet = builder.AddPulumiVirtualNetwork("vnet", "10.0.0.0/16");
var acaSubnet = vnet.AddSubnet("aca-subnet", "10.0.0.0/23");
var peSubnet = vnet.AddSubnet("pe-subnet", "10.0.2.0/24");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env")
    .WithDelegatedSubnet(acaSubnet);

var foundry = builder.AddPulumiFoundry("foundry");   // Sheridan.Aspire.Pulumi.Azure.Foundry
peSubnet.AddPrivateEndpoint(foundry);   // sets publicNetworkAccess: Disabled and registers the private DNS zones

builder.Build().Run();
```

## Features

- `AddPulumiVirtualNetwork`, `AddSubnet`, `WithServiceDelegation`, `WithDelegatedSubnet`.
- Network security groups (`AddPulumiNetworkSecurityGroup`, `WithSecurityRule`, `AllowInbound` / `DenyInbound` / `AllowOutbound` / `DenyOutbound`), public IPs and NAT gateways.
- Private endpoints (`AddPrivateEndpoint`), private link targets and private DNS zones (`GetOrCreatePrivateDnsZone`).
- Network security perimeters (`AddPulumiNetworkSecurityPerimeter`, `WithAccessRule`, `WithNetworkSecurityPerimeter`).

## Notes

- Edit generated resources with `ConfigureInfrastructure`; see the [network overlay notes](https://two4suited.github.io/Sheridan.Aspire.Pulumi/reference/overlays/network/).

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.Network`](https://www.nuget.org/packages/Aspire.Hosting.Azure.Network) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
