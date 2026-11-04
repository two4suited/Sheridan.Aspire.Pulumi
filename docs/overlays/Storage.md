# Azure.Provisioning.Storage overlay (issue #50)

Pulumi port of `Aspire.Hosting.Azure.Provisioning.Storage`. The overlay lets you customize the Storage resources emitted for
`AddPulumiStorage` before the Pulumi program is written. See [provisioning.md](../provisioning.md) for the shared
`ConfigureInfrastructure` machinery.

**Packaging decision:** the overlay lives alongside the hosting project (`Sheridan.Aspire.Pulumi.Azure.Storage`), not in a
sibling opt-in package.

## Roots

| Upstream root | Pulumi property | Resource |
| --- | --- | --- |
| `StorageAccount` | `infra.StorageAccount` | `azure-native:storage:StorageAccount` |

The blob/queue/table service resources, containers, queues and role assignments are reachable with `GetResource(id)` /
`GetResources(type)` (for example `GetResources("azure-native:storage:BlobContainer")`). The callback runs last in
`ContributeResourcesAsync`, after the role assignments are added.

## Example

Property paths are azure-native `storage:StorageAccount` input names (verified with `pulumi package get-schema azure-native`):

```csharp
builder.AddPulumiStorage("storage")
    .WithPulumiStack(azureStack)
    .ConfigureInfrastructure(infra =>
    {
        infra.StorageAccount.Set("allowBlobPublicAccess", false);
        infra.StorageAccount.Set("sku.name", "Standard_LRS");
        infra.StorageAccount.Set("networkRuleSet.defaultAction", "Deny");
        infra.StorageAccount.Set("isHnsEnabled", true);
    });
```

Verified with a temporary AppHost and `aspire publish`: `allowBlobPublicAccess: false` appears in the account's `properties` in `Pulumi.yaml`.

## Unsupported / excluded

- **Compile-time types:** the generated `Pulumi.AzureNative.Storage` classes cannot be used with the YAML pipeline, so `StorageAccount`
  is a property bag addressed by schema names. Typos surface at `pulumi up`.
- **Polyglot (ATS) projection:** no Pulumi equivalent; the callback is C#-only.
- **Experimental opt-in gate:** `ASPIREAZUREPROVISIONING001` is not replicated.
- **`BicepValue<T>` operators:** see provisioning.md; use `PulumiExpr` and `${res.prop}` references.
- The Azurite emulator (`RunAsEmulator`) is not affected by the callback; it only edits the Pulumi-provisioned account.
