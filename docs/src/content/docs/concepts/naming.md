---
title: "Azure resource naming"
sidebar:
  order: 3
---

Integrations used to leave names to Pulumi autonaming (logical name plus a random suffix) or invent them
(`conns<uniqueString>`, `<id>Kv`). The naming layer in `Sheridan.Aspire.Pulumi.Azure.Base`
(`AzureNaming`, `AzureNameKind`) replaces that with names **built from the app name** that **conform to each Azure
resource type's rules**. Names are computed in .NET at generation time (Pulumi YAML has no string helpers) and written as
literal properties (`resourceGroupName`, `resourceName`, `vaultName`, ...). They never contain random values and are
identical on every run.

## Scheme

```
{project}-{resource}[-{suffix}]-{stack}
```

| Segment | Source |
| --- | --- |
| project | `PulumiStackResource.ProjectName` (the Pulumi project / app name, e.g. `aspire-pulumi-packages`) |
| resource | the Aspire resource name (`AddPulumiX("name")`) |
| suffix | optional, chosen by the integration to tell several Azure resources of one Aspire resource apart (`kv`, `acr`, `rg`, `mi`) |
| stack | `PulumiStackResource.StackName`, so environments do not collide in global namespaces |

Rules applied to every segment: lower-cased; `-`, `_`, `.` and spaces become `-` (or are dropped for types that forbid
hyphens); other characters are dropped; repeated hyphens are collapsed; leading/trailing hyphens are removed. Segments are
joined with `-` (or directly, for hyphen-less types such as storage accounts and container registries).

### Long names

1. If the composed name fits the type's `max` and nothing was dropped (other than separators), it is used as is.
2. Otherwise the name is shortened **deterministically**: the longest of project/resource is trimmed one character at a time
   (never below 3 characters), then suffix/stack if still necessary (never below 3, then 2, then 1), and finally a
   6-character lowercase hex hash is appended (`-<hash>`, or directly for hyphen-less types). The hash is the SHA-256 of the full,
   un-shortened segments plus the resource kind, so two different long names never collide and the result is stable.
3. Hash only when lossy: a name that fits is never hashed. "Lossy" means too long or characters were dropped (for example
   non-ASCII letters).
4. Names shorter than the type's `min` are padded with the leading hex digits of the same hash.
5. Types that require a leading letter get the type abbreviation as a prefix when the name would start with a digit
   (`kv-1app-db-dev`).

### Explicit names

`WithAzureName(AzureNameKind.KeyVault, "my-vault", suffix: "kv")` pins a name. It is validated against the same rule
immediately and throws an `ArgumentException` that names the Azure type, every violated rule and the allowed limits
(for example `'Bad_Vault' is not a valid Microsoft.KeyVault/vaults name: invalid character(s) '_' ...`). Helpers that take
a `name` (`AddKeyVault`, `AddResourceGroup`, `AddUserAssignedIdentity`) and `AddKeyVaultSecret`'s secret name validate too.

### Using it from an integration

```csharp
// inside PulumiAzureResource.ContributeResourcesAsync
var vaultName = NameFor(AzureNameKind.KeyVault, "kv");   // override, else composed from project/resource/stack
program.AddKeyVault(kv, resourceGroup, location, name: vaultName);
```

`NameFor` is available on `PulumiAzureResource` once the stack is resolved (while the program is generated) or the resource is
bound with `WithPulumiStack`. For non-`PulumiAzureResource` types implement `IPulumiAzureNamed` and use
`AzureNamingExtensions.NameFor(resource, stack, kind, suffix)`.

## Rule table

Verified against Microsoft's [Naming rules and restrictions for Azure resources](https://learn.microsoft.com/azure/azure-resource-manager/management/resource-name-rules).
"Start"/"End" are the required character class; "No `--`" means consecutive hyphens are rejected (also held for generated
names). Container-apps environment and Search are not on Microsoft's page and use a conservative rule (noted).

| Kind | Resource type | Length | Unique within | Characters | Start | End | No `--` | Abbrev. | Note |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `ResourceGroup` | `Microsoft.Resources/resourceGroups` | 1-90 | Subscription | letters, digits, hyphen, underscore, period | letter/digit | any | no | `rg` | Generated names never end with a period; parentheses are legal but never generated. |
| `UserAssignedIdentity` | `Microsoft.ManagedIdentity/userAssignedIdentities` | 3-128 | ResourceGroup | letters, digits, hyphen, underscore | letter/digit | letter/digit | no | `id` |  |
| `KeyVault` | `Microsoft.KeyVault/vaults` | 3-24 | Global | letters, digits, hyphen | letter | letter/digit | yes | `kv` | Soft-deleted vaults keep their name reserved until purged. |
| `KeyVaultSecret` | `Microsoft.KeyVault/vaults/secrets` | 1-127 | Parent | letters, digits, hyphen | any | any | no | `s` |  |
| `StorageAccount` | `Microsoft.Storage/storageAccounts` | 3-24 | Global | lowercase letters, digits | letter/digit | letter/digit | no | `st` |  |
| `ContainerRegistry` | `Microsoft.ContainerRegistry/registries` | 5-50 | Global | letters, digits | letter/digit | letter/digit | no | `cr` |  |
| `CosmosDbAccount` | `Microsoft.DocumentDB/databaseAccounts` | 3-44 | Global | lowercase letters, digits, hyphen | letter/digit | any | no | `cosmos` |  |
| `SqlServer` | `Microsoft.Sql/servers` | 1-63 | Global | lowercase letters, digits, hyphen | letter/digit | letter/digit | no | `sql` |  |
| `PostgresFlexibleServer` | `Microsoft.DBforPostgreSQL/flexibleServers` | 3-63 | Global | lowercase letters, digits, hyphen | letter/digit | letter/digit | no | `pg` |  |
| `RedisCache` | `Microsoft.Cache/redis` | 1-63 | Global | letters, digits, hyphen | letter/digit | letter/digit | yes | `redis` |  |
| `ServiceBusNamespace` | `Microsoft.ServiceBus/namespaces` | 6-50 | Global | letters, digits, hyphen | letter | letter/digit | no | `sb` |  |
| `EventHubsNamespace` | `Microsoft.EventHub/namespaces` | 6-50 | Global | letters, digits, hyphen | letter | letter/digit | no | `evh` |  |
| `AppConfigurationStore` | `Microsoft.AppConfiguration/configurationStores` | 5-50 | Global | letters, digits, hyphen | letter/digit | letter/digit | yes | `appcs` | Microsoft allows up to two consecutive hyphens; generated names use at most one and overrides are held to the stricter rule. |
| `CognitiveServicesAccount` | `Microsoft.CognitiveServices/accounts` | 2-64 | ResourceGroup | letters, digits, hyphen | letter/digit | letter/digit | no | `cog` |  |
| `CognitiveServicesSubdomain` | `Microsoft.CognitiveServices/accounts (customSubDomainName)` | 2-64 | Global | letters, digits, hyphen | letter/digit | letter/digit | no | `cog` | The custom sub-domain becomes <name>.cognitiveservices.azure.com and is globally unique. |
| `KubernetesCluster` | `Microsoft.ContainerService/managedClusters` | 1-63 | ResourceGroup | letters, digits, hyphen, underscore | letter/digit | letter/digit | no | `aks` |  |
| `ContainerApp` | `Microsoft.App/containerApps` | 2-32 | ResourceGroup | lowercase letters, digits, hyphen | letter | letter/digit | yes | `ca` | Consecutive hyphens are also rejected (generated FQDN). |
| `ContainerAppsEnvironment` | `Microsoft.App/managedEnvironments` | 2-60 | ResourceGroup | lowercase letters, digits, hyphen | letter | letter/digit | yes | `cae` | Not on Microsoft's rules page; a conservative rule (same shape as container apps). |
| `LogAnalyticsWorkspace` | `Microsoft.OperationalInsights/workspaces` | 4-63 | ResourceGroup | letters, digits, hyphen | letter/digit | letter/digit | no | `log` |  |
| `ApplicationInsights` | `Microsoft.Insights/components` | 1-260 | ResourceGroup | letters, digits, hyphen, underscore, period | letter/digit | letter/digit | no | `appi` |  |
| `AppServicePlan` | `Microsoft.Web/serverfarms` | 1-60 | ResourceGroup | letters, digits, hyphen | letter/digit | letter/digit | no | `asp` |  |
| `WebApp` | `Microsoft.Web/sites` | 2-60 | Global | letters, digits, hyphen | letter/digit | letter/digit | no | `app` | Global per default host name (<name>.azurewebsites.net). Function apps use the same rules; the host id only uses the first 32 characters. |
| `SearchService` | `Microsoft.Search/searchServices` | 2-60 | Global | lowercase letters, digits, hyphen | letter/digit | letter/digit | yes | `srch` | Not on Microsoft's rules page; taken from the Azure AI Search service validation. |
| `SignalRService` | `Microsoft.SignalRService/signalR` | 3-63 | Global | letters, digits, hyphen | letter | letter/digit | no | `sigr` |  |
| `WebPubSubService` | `Microsoft.SignalRService/webPubSub` | 3-63 | Global | letters, digits, hyphen | letter | letter/digit | no | `wps` |  |
| `KustoCluster` | `Microsoft.Kusto/clusters` | 4-22 | Region | lowercase letters, digits | letter | letter/digit | no | `adx` |  |
| `VirtualNetwork` | `Microsoft.Network/virtualNetworks` | 2-64 | ResourceGroup | letters, digits, hyphen, underscore, period | letter/digit | letter/digit | no | `vnet` |  |
| `FrontDoorProfile` | `Microsoft.Cdn/profiles` | 1-260 | ResourceGroup | letters, digits, hyphen | letter/digit | letter/digit | no | `afd` |  |
| `ContainerAppJob` | `Microsoft.App/jobs` | 2-32 | ResourceGroup | lowercase letters, digits, hyphen | letter | letter/digit | yes | `job` | Not on Microsoft's rules page; same conservative rule as container apps. |
| `SandboxGroup` | `Microsoft.App/sandboxGroups` | 1-63 | ResourceGroup | lowercase letters, digits, hyphen | letter | letter/digit | yes | `sbx` | Preview type, not on Microsoft's rules page; conservative DNS-label rule. |
| `NetworkSecurityGroup` | `Microsoft.Network/networkSecurityGroups` | 1-80 | ResourceGroup | letters, digits, hyphen, underscore, period | letter/digit | letter/digit | no | `nsg` |  |
| `NatGateway` | `Microsoft.Network/natGateways` | 1-80 | ResourceGroup | letters, digits, hyphen, underscore, period | letter/digit | letter/digit | no | `ng` | Not on Microsoft's rules page; common Microsoft.Network 1-80 rule. |
| `PublicIpAddress` | `Microsoft.Network/publicIPAddresses` | 1-80 | ResourceGroup | letters, digits, hyphen, underscore, period | letter/digit | letter/digit | no | `pip` |  |
| `Subnet` | `Microsoft.Network/virtualNetworks/subnets` | 1-80 | Parent | letters, digits, hyphen, underscore, period | letter/digit | letter/digit | no | `snet` | Unique within the virtual network. |
| `PrivateEndpoint` | `Microsoft.Network/privateEndpoints` | 2-64 | ResourceGroup | letters, digits, hyphen, underscore, period | letter/digit | letter/digit | no | `pe` |  |
| `PrivateDnsVirtualNetworkLink` | `Microsoft.Network/privateDnsZones/virtualNetworkLinks` | 1-80 | Parent | letters, digits, hyphen, underscore, period | letter/digit | letter/digit | no | `link` |  |
| `NetworkSecurityPerimeter` | `Microsoft.Network/networkSecurityPerimeters` | 1-80 | ResourceGroup | letters, digits, hyphen, underscore, period | letter/digit | letter/digit | no | `nsp` | Not on Microsoft's rules page; common Microsoft.Network 1-80 rule. |
| `NetworkSecurityRule` | `Microsoft.Network/networkSecurityGroups/securityRules` | 1-80 | Parent | letters, digits, hyphen, underscore, period | letter/digit | letter/digit | no | `rule` |  |
| `FrontDoorEndpoint` | `Microsoft.Cdn/profiles/afdEndpoints` | 1-50 | Global | letters, digits, hyphen | letter/digit | letter/digit | no | `fde` | Part of the endpoint host name. |
| `FrontDoorOriginGroup` | `Microsoft.Cdn/profiles/originGroups` | 1-50 | Parent | letters, digits, hyphen | letter/digit | letter/digit | no | `og` |  |
| `FrontDoorOrigin` | `Microsoft.Cdn/profiles/originGroups/origins` | 1-50 | Parent | letters, digits, hyphen | letter/digit | letter/digit | no | `origin` |  |
| `FrontDoorRoute` | `Microsoft.Cdn/profiles/afdEndpoints/routes` | 1-50 | Parent | letters, digits, hyphen | letter/digit | letter/digit | no | `route` |  |
| `AksDnsPrefix` | `Microsoft.ContainerService/managedClusters (dnsPrefix)` | 1-54 | Global | letters, digits, hyphen | letter/digit | letter/digit | no | `aks` | Part of the cluster FQDN. |
| `FederatedIdentityCredential` | `Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials` | 3-120 | Parent | letters, digits, hyphen, underscore | letter/digit | letter/digit | no | `fic` |  |
| `RedisEnterpriseCluster` | `Microsoft.Cache/redisEnterprise` | 1-60 | Global | letters, digits, hyphen | letter/digit | letter/digit | yes | `redis` | Azure Managed Redis; the name is part of the host name. |
| `DeploymentScript` | `Microsoft.Resources/deploymentScripts` | 1-90 | ResourceGroup | letters, digits, hyphen, underscore, period | letter/digit | any | no | `ds` | Generated names never end with a period. |
| `ConnectorGateway` | `Microsoft.Web/connectorGateways` | 3-24 | ResourceGroup | lowercase letters, digits | letter | letter/digit | no | `conn` | Preview resource without published naming rules; conservative (the length and character set of the official Bicep's `conns<uniqueString>`). |
| `ApiManagementService` | `Microsoft.ApiManagement/service` | 1-50 | Global | letters, digits, hyphen | letter | letter/digit | yes | `apim` | Global per gateway host name (`<name>.azure-api.net`). |

Generated names are always lowercase even where Azure accepts upper case.

## Applied so far

| Resource | Azure name | Property | Pulumi logical id (unchanged) |
| --- | --- | --- | --- |
| Per-resource resource group (`PulumiAzureResource` without a shared group) | `NameFor(ResourceGroup, "rg")` | `resourceGroupName` | `<id>Rg` |
| `AddPulumiAzureResourceGroup("x")` | `NameFor(ResourceGroup)` | `resourceGroupName` | `<id>` |
| `AddPulumiAzureUserAssignedIdentity("x")` | `NameFor(UserAssignedIdentity)` | `resourceName` | `<id>` |
| Key Vaults created by `AddPulumiAzureKeyVault`, and the vaults of Cosmos DB (access keys), PostgreSQL (password auth) and Redis (access keys) | `NameFor(KeyVault[, "kv"])` | `vaultName` | unchanged |
| Azure OpenAI account (`AddPulumiOpenAI`) | `NameFor(CognitiveServicesAccount)`; custom sub-domain `NameFor(CognitiveServicesSubdomain)` (or `CustomSubDomainName`, validated) | `accountName`, `customSubDomainName` | `<id>` |
| Foundry account (`AddPulumiFoundry`) | same as OpenAI | `accountName`, `customSubDomainName` | `<id>` |
| Foundry project Application Insights (when none is given) | `NameFor(ApplicationInsights, "<project>-appi")` | `resourceName` | `<id><project>Ai` |
| Connector Namespace gateway | `NameFor(ConnectorGateway)` as a literal in the ARM template variable | template variable `gatewayName` | `<id>` |
| Entra ID app registrations (`AddPulumiEntraId`, `AddPulumiAzureAdAppRegistration`) | `PulumiEntraNaming.DisplayName`: `{project}-{resource}-{stack}` (readable, up to 256 characters, `WithDisplayName` overrides) | `displayName` | unchanged |

Foundry project names, connection names, deployment names and the capability host names (`foundry-caphost`, `<project>-ch`) are
already deterministic and are unchanged.

Not yet applied (still provider autonaming or their own scheme; follow-ups): all other integrations, the compute environments'
identities/registries/workspaces, and role assignment names (azure-native requires a GUID, which the provider already generates
and keeps in state; changing it would replace every assignment).

### Compute and platform integrations

| Resource | Name composed from | Property | Example (project `aspire-pulumi-packages`, stack `azure-test`, resource `aca-env`) |
| --- | --- | --- | --- |
| ACA environment / its registry, workspace, identity | `NameFor(ContainerAppsEnvironment)`, `(ContainerRegistry,"acr")`, `(LogAnalyticsWorkspace,"law")`, `(UserAssignedIdentity,"admin-mi")` | `environmentName`, `registryName`, `workspaceName`, `resourceName` | `aspire-pulumi-packages-aca-env-azure-test`, `aspirepulumipackagesacaenvacrazuretest`, `...-aca-env-law-azure-test`, `...-aca-env-mi-azure-test` |
| Container app / job | Aspire compute resource name only (`ComposeScoped`; unique per environment), also used for service discovery host names | `containerAppName`, `jobName` | `api` (hashed to 32 chars only when it does not fit) |
| App Service environment: registry, identities, plan, workspace, App Insights, dashboard app | `NameFor(..., "acr"/"mi"/"contributor-mi"/"plan"/"law"/"appi"/"dashboard")` | `registryName`, `resourceName`, `name`, `workspaceName`, `resourceName`, `name` | `aspire-pulumi-packages-ase-plan-azure-test` |
| App Service web app | `{project}-{compute resource}-{stack}` (global `azurewebsites.net` host) | `name` | `aspire-pulumi-packages-api-azure-test` |
| Functions implicit host storage | `{project}{functions project or "functions"}host{stack}` (the Aspire resource name `funcstorage<hash>` is only the Pulumi logical id) | `accountName` | hashed to 24 characters when too long |
| AKS: cluster, DNS prefix, registry, workload / control-plane identities, federated credentials | `NameFor(KubernetesCluster)`, `(AksDnsPrefix)`, `(ContainerRegistry,"acr")`, `(UserAssignedIdentity,"mi"/"cp-mi")`, `{app}-fedcred` | `resourceName`, `dnsPrefix`, `registryName`, `resourceName`, `federatedIdentityCredentialResourceName` | |
| Kubernetes object names (DNS-1123, 63) | Aspire resource name; more than 63 characters is trimmed with a 6-character hash (`-service` is accounted for) | `metadata.name` | |
| Sandbox group, registry, identity | `NameFor(SandboxGroup)`, `(ContainerRegistry,"acr")`, `(UserAssignedIdentity,"admin-mi")` | `resourceName`, `registryName`, `resourceName` | |
| `AddPulumiAzureContainerRegistry` | `NameFor(ContainerRegistry)` | `registryName` | |
| Log Analytics workspace; Application Insights (+ implicit workspace) | `NameFor(LogAnalyticsWorkspace[,"law"])`, `NameFor(ApplicationInsights)` | `workspaceName`, `resourceName` | |
| Front Door profile, endpoint, origin group, origin, route | `NameFor(FrontDoorProfile)`; endpoint `{project}-{origin}-{stack}`; others scoped to the origin name | `profileName`, `endpointName`, `originGroupName`, `originName`, `routeName` | |
| Network: vnet, subnet, NSG (+rules), NAT, public IP, private endpoint (+connection), DNS vnet link, NSP | `NameFor(...)`; subnets and generated rule names are scoped (Aspire name, sanitized); the DNS zone name itself is fixed by the service (`privatelink.*`) | `virtualNetworkName`, `subnetName`, `networkSecurityGroupName`, `securityRuleName`, `natGatewayName`, `publicIpAddressName`, `privateEndpointName`, `virtualNetworkLinkName`, `networkSecurityPerimeterName` | `aspire-pulumi-packages-vnet-azure-test` |

### Messaging and configuration integrations

| Resource | Name composed from | Property | Example (project `aspire-pulumi-packages`, stack `azure-test`) |
| --- | --- | --- | --- |
| Service Bus namespace | `NameFor(ServiceBusNamespace)` | `namespaceName` | `aspire-pulumi-packages-sb-azure-test` (resource `sb`) |
| Event Hubs namespace | `NameFor(EventHubsNamespace)` | `namespaceName` | `aspire-pulumi-packages-evh-azure-test` (resource `evh`) |
| App Configuration store | `NameFor(AppConfigurationStore)` | `configStoreName` | `aspire-pulumi-packages-appconfig-azure-test` |
| SignalR service | `NameFor(SignalRService)` | `resourceName` | `aspire-pulumi-packages-signalr-azure-test` |
| Web PubSub service | `NameFor(WebPubSubService)` | `resourceName` | `aspire-pulumi-packages-pubsub-azure-test` |
| Azure AI Search service | `NameFor(SearchService)` | `searchServiceName` | `aspire-pulumi-packages-search-azure-test` |
| SQL server admin identity | `NameFor(UserAssignedIdentity, "admin-mi")` | `resourceName` | |

Key Vaults (`AddPulumiAzureKeyVault` and the implicit vaults of Cosmos DB, PostgreSQL and Redis) were already named. Queues, topics,
subscriptions, event hubs, consumer groups and hubs use the names the caller passes. Still on provider autonaming or invented names
(owned by the data/AI integrations): Cosmos DB account, SQL server, PostgreSQL server, Redis cache, Kusto cluster, Azure OpenAI and
Foundry accounts, and the connector namespace gateway (`conns<uniqueString>`).

An explicit subnet name passed to `AddSubnet(..., subnetName)` and explicit security rule names are validated against the rules above. Not touched: node pool names (fixed `system`/`workload` or caller supplied),
the Front Door/NSP profile constants (`defaultProfile`), private DNS zone group name (`default`), and data-plane sandbox names (which carry a deploy id).

### Data integrations

| Resource | Name composed from | Property | Example (project `aspire-pulumi-packages`, stack `azure-test`) |
| --- | --- | --- | --- |
| Cosmos DB account (+ Key Vault in access-key mode) | `NameFor(CosmosDbAccount)`, `(KeyVault,"kv")` | `accountName`, `vaultName` | `aspire-pulumi-packages-cosmos-azure-test` |
| SQL server, admin identity, role deployment scripts | `NameFor(SqlServer)`, `(UserAssignedIdentity,"admin-mi")`, `(DeploymentScript,"{db}-{env}-roles")` | `serverName`, `resourceName`, `scriptName` | `aspire-pulumi-packages-sql-azure-test`, `...-sql-admin-mi-azure-test` |
| PostgreSQL flexible server (+ Key Vault in password mode) | `NameFor(PostgresFlexibleServer)`, `(KeyVault,"kv")` | `serverName`, `vaultName` | `aspire-pulumi-packages-pg-azure-test` |
| Redis cache (+ Key Vault) | `NameFor(RedisCache)`, `(KeyVault,"kv")` | `name`, `vaultName` | `aspire-pulumi-packages-cache-azure-test` |
| Managed Redis | `NameFor(RedisEnterpriseCluster)` | `clusterName` | `aspire-pulumi-packages-mredis-azure-test` |
| Storage account | `NameFor(StorageAccount)` | `accountName` | `aspistoraazuretest279b9b` (hashed to 24) |
| Kusto cluster | `NameFor(KustoCluster)` | `clusterName` | `aspkustazuretest528b82` (hashed to 22) |

Databases, containers, queues, elastic pools and Kusto databases keep the names the caller gave them (they are scoped to their
parent). Existing-resource modes (`AsExisting`/`PublishAsExisting`) are untouched. Because the SQL server name is now explicit,
an existing stack's autonamed server, Cosmos/PostgreSQL/Redis/Kusto account and admin identity are **replaced** on the next
`pulumi up` (data loss for stateful resources) unless pinned first with `WithAzureName(AzureNameKind.X, "<current name>")`.

## Replacement implications

Resource names are immutable in Azure. Adding an explicit name to a resource that existed with an autogenerated name makes
Pulumi **replace** it (resource group, user-assigned identity, Key Vault). Replacing a resource group deletes everything in it,
so existing stacks should either pin the current names with `WithAzureName(...)` before upgrading, or accept the replacement
for fresh environments. Key Vault replacement also leaves the old vault soft-deleted (the name stays reserved until purged).
