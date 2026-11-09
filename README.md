# Sheridan.Aspire.Pulumi

Aspire hosting integrations that provision Azure infrastructure with [Pulumi](https://www.pulumi.com) instead of Bicep/ARM. Each package mirrors the official `Aspire.Hosting.Azure.*` integration (`AddPulumiX` instead of `AddAzureX`, same `WithX` calls), so swapping Bicep for Pulumi is a drop-in change in your AppHost.

**Documentation: https://two4suited.github.io/Sheridan.Aspire.Pulumi/**

## Quick start

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.AppContainers
dotnet add package Sheridan.Aspire.Pulumi.Azure.CosmosDB
```

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");
var cosmos = builder.AddPulumiCosmosDB("cosmos");
cosmos.AddCosmosDatabase("appdb").AddContainer("items", "/id");

builder.AddProject<Projects.MyApi>("api")
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(acaEnv)
    .WithReference(cosmos);

builder.Build().Run();
```

Then `aspire publish` to generate the Pulumi program, or `aspire deploy` to deploy it. Prerequisites: the Aspire CLI, the Pulumi CLI (`pulumi login`) and an Azure subscription.

## Packages

Packages are published to [nuget.org](https://www.nuget.org/profiles/two4suited) (and mirrored to GitHub Packages) when a `vX.Y.Z` tag is pushed, so a plain `dotnet add package <PackageId>` works with no extra feed. Each package's page on nuget.org is `https://www.nuget.org/packages/<PackageId>`.

The package id equals the folder name under `src/`, and namespaces match the package ids. Each package has its own README with usage details.

| Area | Package | Provisions |
| --- | --- | --- |
| Core | [`Sheridan.Aspire.Pulumi`](src/Sheridan.Aspire.Pulumi) | Stack resource, program/YAML generation, Pulumi CLI pipeline (`AddPulumiStack`, `DependsOnStack`) |
|  | [`Sheridan.Aspire.Pulumi.Provisioning`](src/Sheridan.Aspire.Pulumi.Provisioning) | `ConfigureInfrastructure` support to tweak generated resources |
|  | [`Sheridan.Aspire.Pulumi.Azure`](src/Sheridan.Aspire.Pulumi.Azure) | Shared Azure interfaces (managed identity, container app customization) |
|  | [`Sheridan.Aspire.Pulumi.Azure.Base`](src/Sheridan.Aspire.Pulumi.Azure.Base) | Resource groups, identities, role definitions, naming |
| Compute | [`Sheridan.Aspire.Pulumi.Azure.AppContainers`](src/Sheridan.Aspire.Pulumi.Azure.AppContainers) | Azure Container Apps environment |
|  | [`Sheridan.Aspire.Pulumi.Azure.AppService`](src/Sheridan.Aspire.Pulumi.Azure.AppService) | Azure App Service environment |
|  | [`Sheridan.Aspire.Pulumi.Azure.Kubernetes`](src/Sheridan.Aspire.Pulumi.Azure.Kubernetes) | Azure Kubernetes Service |
|  | [`Sheridan.Aspire.Pulumi.Azure.Sandboxes`](src/Sheridan.Aspire.Pulumi.Azure.Sandboxes) | Container Apps Sandboxes |
|  | [`Sheridan.Aspire.Pulumi.Azure.Functions`](src/Sheridan.Aspire.Pulumi.Azure.Functions) | Azure Functions projects |
|  | [`Sheridan.Aspire.Pulumi.Azure.FrontDoor`](src/Sheridan.Aspire.Pulumi.Azure.FrontDoor) | Azure Front Door |
|  | [`Sheridan.Aspire.Pulumi.Azure.ContainerRegistry`](src/Sheridan.Aspire.Pulumi.Azure.ContainerRegistry) | Azure Container Registry |
| Data | [`Sheridan.Aspire.Pulumi.Azure.CosmosDB`](src/Sheridan.Aspire.Pulumi.Azure.CosmosDB) | Azure Cosmos DB |
|  | [`Sheridan.Aspire.Pulumi.Azure.Sql`](src/Sheridan.Aspire.Pulumi.Azure.Sql) | Azure SQL |
|  | [`Sheridan.Aspire.Pulumi.Azure.PostgreSQL`](src/Sheridan.Aspire.Pulumi.Azure.PostgreSQL) | PostgreSQL flexible server |
|  | [`Sheridan.Aspire.Pulumi.Azure.Redis`](src/Sheridan.Aspire.Pulumi.Azure.Redis) | Azure Cache for Redis / Managed Redis |
|  | [`Sheridan.Aspire.Pulumi.Azure.Storage`](src/Sheridan.Aspire.Pulumi.Azure.Storage) | Blobs, queues, tables, Data Lake (Azurite emulator or existing account for local runs) |
|  | [`Sheridan.Aspire.Pulumi.Azure.Kusto`](src/Sheridan.Aspire.Pulumi.Azure.Kusto) | Azure Data Explorer |
|  | [`Sheridan.Aspire.Pulumi.Azure.Search`](src/Sheridan.Aspire.Pulumi.Azure.Search) | Azure AI Search |
| Messaging | [`Sheridan.Aspire.Pulumi.Azure.ServiceBus`](src/Sheridan.Aspire.Pulumi.Azure.ServiceBus) | Service Bus |
|  | [`Sheridan.Aspire.Pulumi.Azure.EventHubs`](src/Sheridan.Aspire.Pulumi.Azure.EventHubs) | Event Hubs |
|  | [`Sheridan.Aspire.Pulumi.Azure.SignalR`](src/Sheridan.Aspire.Pulumi.Azure.SignalR) | SignalR Service |
|  | [`Sheridan.Aspire.Pulumi.Azure.WebPubSub`](src/Sheridan.Aspire.Pulumi.Azure.WebPubSub) | Web PubSub |
|  | [`Sheridan.Aspire.Pulumi.Azure.ConnectorNamespace`](src/Sheridan.Aspire.Pulumi.Azure.ConnectorNamespace) | Connector namespaces (preview) |
| AI | [`Sheridan.Aspire.Pulumi.Azure.CognitiveServices`](src/Sheridan.Aspire.Pulumi.Azure.CognitiveServices) | Azure OpenAI |
|  | [`Sheridan.Aspire.Pulumi.Azure.Foundry`](src/Sheridan.Aspire.Pulumi.Azure.Foundry) | Microsoft Foundry: projects, deployments, agents |
| Config & security | [`Sheridan.Aspire.Pulumi.Azure.KeyVault`](src/Sheridan.Aspire.Pulumi.Azure.KeyVault) | Key Vault |
|  | [`Sheridan.Aspire.Pulumi.Azure.AppConfiguration`](src/Sheridan.Aspire.Pulumi.Azure.AppConfiguration) | App Configuration |
|  | [`Sheridan.Aspire.Pulumi.Azure.ApiManagement`](src/Sheridan.Aspire.Pulumi.Azure.ApiManagement) | API Management (core: APIs, OpenAPI import, backends, products, policies) |
|  | [`Sheridan.Aspire.Pulumi.Azure.EntraId`](src/Sheridan.Aspire.Pulumi.Azure.EntraId) | Entra ID app registrations: scopes, app roles, API permissions and consent, federated credentials (`AddPulumiEntraId`, mirrors `Aspire.Hosting.Azure.EntraId`; preferred) |
|  | [`Sheridan.Aspire.Pulumi.Entra`](src/Sheridan.Aspire.Pulumi.Entra) | Lower-level Azure AD app registration through the `azuread` provider (`AddPulumiAzureAdAppRegistration`); prefer `EntraId` |
| Networking | [`Sheridan.Aspire.Pulumi.Azure.Network`](src/Sheridan.Aspire.Pulumi.Azure.Network) | VNets, subnets, private endpoints, private DNS |
| Monitoring | [`Sheridan.Aspire.Pulumi.Azure.OperationalInsights`](src/Sheridan.Aspire.Pulumi.Azure.OperationalInsights) | Log Analytics workspaces |
|  | [`Sheridan.Aspire.Pulumi.Azure.ApplicationInsights`](src/Sheridan.Aspire.Pulumi.Azure.ApplicationInsights) | Application Insights |

## How it works

`AddPulumiStack` declares a Pulumi stack in your AppHost. There is no separate Pulumi project: the Aspire pipeline generates it.

- `aspire publish` writes `Pulumi.yaml` and `Pulumi.<stack>.yaml` (which imports the ESC environment) to `<output>/<name>/pulumi/`.
- `aspire deploy` checks `pulumi whoami`, creates the ESC environment and stack if missing, and runs `pulumi up`. State lives in Pulumi Cloud.
- `aspire destroy` (or `aspire do pulumi-destroy-<name>`) runs `pulumi destroy`.

The stack name defaults to the lower-cased Aspire environment, so `aspire deploy -e prod` targets stack `prod`; pass a second argument to `AddPulumiStack` to pin it. Both stack and ESC environments can be overridden on the command line:

```bash
aspire deploy -- --Pulumi:Stack=qa --Pulumi:Esc=shared/qa,myproj/qa
```

(also settable via config or the `Pulumi__Stack` / `Pulumi__Esc` environment variables). Use `.WithEscEnvironment("project/env1", "project/env2")` to import existing ESC environments (later ones override earlier ones); with none specified, `<name>/<stack>` is used and created on deploy if missing. For how deploys are ordered, see the [core package README](src/Sheridan.Aspire.Pulumi/README.md).

## Resource naming

Azure names are built from the app (Pulumi project) name, the Aspire resource name and the stack name, shortened deterministically (with a hash only when lossy) to fit each resource type's limits. Pin one with `WithAzureName(AzureNameKind.X, "name")`. See [docs/naming.md](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/).

## Multiple stacks

Split resources across Pulumi stacks (own project directory, own ESC environment) by adding more than one `AddPulumiStack` and binding each resource with `.WithPulumiStack(...)`:

```csharp
var identityStack = builder.AddPulumiStack("my-app-identity", "identity-dev")
    .WithProjectName("my-app")
    .WithOrganization("my-org")
    .WithEscEnvironment("identity/dev");

builder.AddPulumiAzureAdAppRegistration("app-registration")
    .WithPulumiStack(identityStack);

var azureStack = builder.AddPulumiStack("my-app", "azure-dev")
    .WithProjectName("my-app")
    .WithOrganization("my-org")
    .WithEscEnvironment("azure/dev")
    .DependsOnStack(identityStack);

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env")
    .WithPulumiStack(azureStack);
```

- With more than one stack in the model, every stack-bound resource must call `.WithPulumiStack(...)` explicitly.
- `.WithProjectName(...)` groups several stacks under one Pulumi project in the backend (here `my-org/my-app/identity-dev` and `my-org/my-app/azure-dev`, so stack names must differ); it defaults to the Aspire resource name.
- `.DependsOnStack(...)` makes `aspire deploy` fully deploy the dependency before provisioning this stack. It only orders the provision/deploy steps and has no effect on `aspire publish`. References between resources in different stacks create the same ordering automatically.

## Breaking changes

- **Entra ID (`Sheridan.Aspire.Pulumi.Azure.EntraId`, issue #144):** `WithRedirectUris`, `WithApplicationOwners`, `WithServicePrincipalOwners`, `WithOwners` and `WithIdTokenIssuance` now throw `InvalidOperationException` on existing applications instead of silently doing nothing, and `AsExistingApplication` throws if such settings were applied first. `WithReference` gains optional connection and downstream-API key parameters. See [decision 0002](https://two4suited.github.io/Sheridan.Aspire.Pulumi/reference/decisions/0002-entraid-upstream-sync/).

## Repository layout

- `src/`: one class library per package (`Sheridan.Aspire.Pulumi*`).
- `tests/`: unit tests; run with `dotnet test Sheridan.Aspire.Pulumi.slnx`.
- `docs/`: the [documentation site](https://two4suited.github.io/Sheridan.Aspire.Pulumi/) (Astro Starlight; run `npm install && npm run dev` in `docs/`), including the [naming scheme](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/naming/), [provisioning / `ConfigureInfrastructure`](https://two4suited.github.io/Sheridan.Aspire.Pulumi/concepts/provisioning/) and per-service overlay notes, and the [release notes](https://two4suited.github.io/Sheridan.Aspire.Pulumi/reference/release-notes/).

See [CLAUDE.md](CLAUDE.md) for how new resource integrations are added.

## License

[MIT](LICENSE)
