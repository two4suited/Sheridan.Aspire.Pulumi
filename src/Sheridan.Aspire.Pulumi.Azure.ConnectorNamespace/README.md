# Sheridan.Aspire.Pulumi.Azure.ConnectorNamespace

_Connector Namespace_ — part of [Sheridan.Aspire.Pulumi](../../README.md).

`Sheridan.Aspire.Pulumi.Azure.ConnectorNamespace` ports `Aspire.Hosting.Azure.ConnectorNamespace` (preview). **azure-native has no `Microsoft.Web/connectorGateways` resource** (confirmed with `pulumi package get-schema azure-native`), so the namespace is provisioned as a single `azure-native:resources:Deployment` (an ARM template deployment) containing exactly the resource types, `2026-05-01-preview` API version and properties of the official generated Bicep: the gateway (`conns<uniqueString>`, system-assigned identity), connections, connection access policies, `mcpserverConfigs` (`ManagedMcpServer`, state `Enabled`) and MCP access policies. Outputs: `Id`, `Name`, `PrincipalId`, `TenantId`.

```csharp
var connectors = builder.AddPulumiConnectorNamespace("connectors").WithPulumiStack(azureStack).WithResourceGroup(rg);
var office = connectors.AddConnection("office", "office365")
    .WithAccessPolicy("owner", new() { ObjectId = "<guid>", TenantId = "<guid>" })
    .WithIdentityAccessPolicy("app", appIdentity);   // a PulumiAzureUserAssignedIdentityResource in the same stack
connectors.AddMcpServerConfig("mcp", new() { Description = "Mail tools" })
    .WithConnector("office-tools", office, new() { Operations = [new() { Name = "SendEmail" }] })
    .WithAccessPolicy("user", new() { ObjectId = "<guid>", TenantId = "<guid>", PrincipalType = AzureConnectorNamespaceMcpAccessPolicyPrincipalType.User });

builder.AddProject<Projects.MyApi>("api").WithReference(office);   // {name}__connectorGatewayName / {name}__connectionName
```

`ConfigureInfrastructure(infra => ...)` exposes the `Deployment` root plus the ARM template's `TemplateResources`/`Gateway` maps (ARM property names, not azure-native names). `PublishAsExisting(name, resourceGroup)` / `AsExisting(name, resourceGroup)` on the namespace uses an existing gateway: the template drops the gateway resource (children are deployed under the existing gateway name, in that resource group, without `dependsOn` on it) and the outputs read the existing gateway; connection- and MCP-config-level `AsExisting` work as before. Not ported: the Aspire polyglot (ATS) exports. **Provider gap (checked against the current azure-native schema; azapi is not available as a Pulumi package):** azure-native has no `connectorGateways` resource family (the only similar `azure-native:web:ConnectionGateway` is the unrelated Logic Apps `Microsoft.Web/connectionGateways`), so no typed resource can replace the ARM-template Deployment; per-resource diffs are therefore not available, only a diff of the template. Revisit when azure-native publishes the `2026-05-01-preview` Microsoft.Web specs. Note that connections using OAuth must still be consented in the portal after deployment, as with the Bicep version. The ARM template route was verified by comparing `aspire publish` output against the official Bicep, not by a real deployment.
