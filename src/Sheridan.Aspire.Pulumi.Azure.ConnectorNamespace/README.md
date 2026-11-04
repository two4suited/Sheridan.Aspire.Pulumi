# Sheridan.Aspire.Pulumi.Azure.ConnectorNamespace

Azure connector namespaces (preview) provisioned by Pulumi, a port of `Aspire.Hosting.Azure.ConnectorNamespace`.

`AddPulumiConnectorNamespace` provisions a connector gateway with connections, access policies and managed MCP server configs. azure-native has no `Microsoft.Web/connectorGateways` resource, so the namespace is a single `azure-native:resources:Deployment` (ARM template) with exactly the resource types, API version (`2026-05-01-preview`) and properties of the official Bicep.

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.ConnectorNamespace
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var connectors = builder.AddPulumiConnectorNamespace("connectors");
var office = connectors.AddConnection("office", "office365")
    .WithAccessPolicy("owner", new() { ObjectId = "<guid>", TenantId = "<guid>" });

connectors.AddMcpServerConfig("mcp", new() { Description = "Mail tools" })
    .WithConnector("office-tools", office, new() { Operations = [new() { Name = "SendEmail" }] });

builder.AddProject<Projects.MyApi>("api")
    .WithReference(office);   // {name}__connectorGatewayName / {name}__connectionName

builder.Build().Run();
```

## Features

- Outputs: `Id`, `Name`, `PrincipalId`, `TenantId`.
- `WithIdentityAccessPolicy(name, identity)` grants a Pulumi user-assigned identity access; MCP configs add `WithConnector` and `WithAccessPolicy`.
- `PublishAsExisting(name, resourceGroup)` / `AsExisting(...)` use an existing gateway; connection- and MCP-level `AsExisting` also work.
- `ConfigureInfrastructure` exposes the `Deployment` root and the template's `TemplateResources` / `Gateway` maps (ARM property names).

## Notes

- Provider gap: only a diff of the template is available, not per-resource diffs. Revisit when azure-native publishes the preview Microsoft.Web specs.
- Connections that use OAuth must still be consented in the portal after deployment, as with the Bicep version.
- Verified by comparing `aspire publish` output against the official Bicep, not by a deployment. The Aspire polyglot (ATS) exports are not ported.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.ConnectorNamespace`](https://www.nuget.org/packages/Aspire.Hosting.Azure.ConnectorNamespace) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/).
- Resource naming: [naming guide](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).
