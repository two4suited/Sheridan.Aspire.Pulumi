# Sheridan.Aspire.Pulumi.Azure.Base

_Resource groups_ — part of [Sheridan.Aspire.Pulumi](../../README.md).

Resources built on the shared `Sheridan.Aspire.Pulumi.Azure.Base` layer (e.g. `AddPulumiAzureUserAssignedIdentity`) create their own resource group by default. To put several of them in one shared resource group instead, provision it explicitly and point each resource at it with `WithResourceGroup`:

```csharp
var sharedRg = builder.AddPulumiAzureResourceGroup("shared-rg")
    .WithPulumiStack(azureStack);

var workloadIdentity = builder.AddPulumiAzureUserAssignedIdentity("workload-identity")
    .WithPulumiStack(azureStack)
    .WithResourceGroup(sharedRg);
```

Both resources must be bound to the same Pulumi stack — `WithResourceGroup` is a same-program `${sharedRg.name}` reference, not a cross-stack output lookup — and binding them to different stacks throws a clear error instead of generating broken YAML. `WithExistingResourceGroup("name")` is still available for pointing at a resource group Pulumi doesn't manage at all.
