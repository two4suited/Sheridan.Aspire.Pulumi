# Network overlay (issue #40)

Pulumi counterpart of `Aspire.Hosting.Azure.Provisioning.Network`. Overlays the Network hosting integration
(`Sheridan.Aspire.Pulumi.Azure.Network`). General mechanism: [../provisioning.md](../provisioning.md).
(The sibling private DNS overlay is documented in [PrivateDns.md](PrivateDns.md).)

**Packaging decision:** the overlay lives alongside the hosting project (no sibling `...Provisioning` package, no opt-in gate).
Hooks were missing and were added (`PulumiNetworkInfrastructure.cs`); callbacks run last in each resource's `ContributeResourcesAsync`.
Upstream has one `ConfigureInfrastructure` per Aspire resource, with the root matching that resource, so we do the same.

## Roots

| Upstream root | Builder type (`ConfigureInfrastructure` on) | Property | azure-native resource |
| --- | --- | --- | --- |
| `VirtualNetwork` | `PulumiVirtualNetworkResource` (`AddPulumiVirtualNetwork`) | `VirtualNetwork` (+ `Subnets`) | `azure-native:network:VirtualNetwork` (+ `Subnet`) |
| `NetworkSecurityGroup` | `PulumiNetworkSecurityGroupResource` | `NetworkSecurityGroup` (+ `SecurityRules`) | `azure-native:network:NetworkSecurityGroup` (+ `SecurityRule`) |
| `NatGateway` | `PulumiNatGatewayResource` | `NatGateway` | `azure-native:network:NatGateway` |
| `PublicIPAddress` | `PulumiPublicIPAddressResource` | `PublicIPAddress` | `azure-native:network:PublicIPAddress` |
| `PrivateEndpoint` | `PulumiPrivateEndpointResource` (`AddPrivateEndpoint`) | `PrivateEndpoint` (+ `PrivateDnsZoneGroup`) | `azure-native:network:PrivateEndpoint` (+ `PrivateDnsZoneGroup`) |
| `NetworkSecurityPerimeter` | `PulumiNetworkSecurityPerimeterResource` | `NetworkSecurityPerimeter` (+ `Profile`, `AccessRules`, `Associations`) | `azure-native:network:NetworkSecurityPerimeter` (+ `NspProfile`, `NspAccessRule`, `NspAssociation`) |

Each root is only available inside the callback of its own resource; resources of other builders are reachable by id with
`GetResource(id)` only when they share the same Pulumi program (stack).

## Example

```csharp
builder.AddPulumiVirtualNetwork("vnet", "10.0.0.0/16")
    .WithPulumiStack(stack)
    .ConfigureInfrastructure(infra =>
    {
        infra.VirtualNetwork.Set("enableDdosProtection", true);
        infra.VirtualNetwork.Set("flowTimeoutInMinutes", 10);
        foreach (var subnet in infra.Subnets) subnet.Set("privateEndpointNetworkPolicies", "Enabled");
    });

builder.AddPulumiNetworkSecurityGroup("nsg").WithPulumiStack(stack)
    .ConfigureInfrastructure(infra => infra.NetworkSecurityGroup.Set("flushConnection", true));

builder.AddPulumiPublicIPAddress("pip").WithPulumiStack(stack)
    .ConfigureInfrastructure(infra => infra.PublicIPAddress.Set("idleTimeoutInMinutes", 10));
```

Paths are inputs per `pulumi package get-schema azure-native`: `VirtualNetwork` (`enableDdosProtection`, `flowTimeoutInMinutes`,
`dhcpOptions`, `encryption`, `addressSpace`), `NetworkSecurityGroup` (`flushConnection`, `securityRules`), `NatGateway`
(`idleTimeoutInMinutes`, `zones`), `PublicIPAddress` (`idleTimeoutInMinutes`, `dnsSettings`, `zones`, `ddosSettings`),
`PrivateEndpoint` (`customNetworkInterfaceName`, `ipConfigurations`), `NetworkSecurityPerimeter` (`tags`).
Verified with `aspire publish` for VirtualNetwork, NetworkSecurityGroup and PublicIPAddress (the emitted `Pulumi.yaml` contained
`enableDdosProtection`, `flushConnection`, `idleTimeoutInMinutes`); NatGateway, PrivateEndpoint and NetworkSecurityPerimeter use the same
code path and were only build-checked.

## Unsupported / excluded

- Compile-time types: upstream exposes the `Azure.Provisioning.Network` SDK models; we emit YAML, so properties are untyped dotted paths
  and typos surface at `pulumi up` (ADR 0001).
- Polyglot (ATS) projection: no Pulumi equivalent; C#-only.
- Experimental opt-in gate (`ASPIREAZUREPROVISIONING001`): not replicated.
- BicepValue operators/expressions: use `${...}` references and `PulumiExpr`.
- Subnets/NSGs are separate Aspire resources upstream's overlay can reach through the shared infrastructure; here a subnet is edited via
  `Subnets` of its virtual network and a NSG via its own builder.
