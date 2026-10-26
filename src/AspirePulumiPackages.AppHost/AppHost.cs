using AspirePulumiPackages.Pulumi;
using AspirePulumiPackages.Pulumi.Azure;
using AspirePulumiPackages.Pulumi.Azure.AppConfiguration;
using AspirePulumiPackages.Pulumi.Azure.AppContainers;
using AspirePulumiPackages.Pulumi.Azure.Base;
using AspirePulumiPackages.Pulumi.Azure.CognitiveServices;
using AspirePulumiPackages.Pulumi.Azure.EntraId;
using AspirePulumiPackages.Pulumi.Azure.Kusto;
using AspirePulumiPackages.Pulumi.Azure.Search;
using AspirePulumiPackages.Pulumi.Azure.SignalR;
using AspirePulumiPackages.Pulumi.Azure.Storage;
using AspirePulumiPackages.Pulumi.Azure.WebPubSub;

var builder = DistributedApplication.CreateBuilder(args);

var appName = "aspire-pulumi-packages";

var entraStack = builder.AddPulumiStack($"{appName}-entra", "entra-test")
    .WithProjectName(appName)
    .WithOrganization("two4suited")
    .WithEscEnvironment("entra/test");

var appRegistration = builder.AddPulumiEntraId("app-registration")
    .WithPulumiStack(entraStack);

var azureStack = builder.AddPulumiStack(appName, "azure-test")
    .WithProjectName(appName)
    .WithOrganization("two4suited")
    .WithEscEnvironment("azure/test")
    .DependsOnStack(entraStack);

var sharedRg = builder.AddPulumiAzureResourceGroup("shared-rg")
    .WithPulumiStack(azureStack);

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env")
    .WithPulumiStack(azureStack)
    .WithResourceGroup(sharedRg);

var cosmos = builder.AddPulumiCosmosDB("cosmos")
    .WithPulumiStack(azureStack)
    .WithResourceGroup(sharedRg)
    .RunAsEmulator();
cosmos.AddCosmosDatabase("appdb").AddContainer("items", "/id");

var storage = builder.AddPulumiStorage("storage")
    .WithPulumiStack(azureStack)
    .WithResourceGroup(sharedRg)
    .RunAsEmulator();
var blobs = storage.AddBlobs("blobs");
blobs.AddBlobContainer("documents");

var openai = builder.AddPulumiOpenAI("openai")
    .WithPulumiStack(azureStack)
    .WithResourceGroup(sharedRg)
    .ConfigureInfrastructure(infra => infra.CognitiveServicesAccount.Set("properties.publicNetworkAccess", "Enabled"));
var chat = openai.AddDeployment("chat", "gpt-4o", "2024-08-06");

var appConfig = builder.AddPulumiAppConfiguration("appconfig")
    .WithPulumiStack(azureStack)
    .WithResourceGroup(sharedRg)
    .RunAsEmulator()
    .ConfigureInfrastructure(infra => infra.AppConfigurationStore.Set("softDeleteRetentionInDays", 7));

var signalr = builder.AddPulumiSignalR("signalr")
    .WithPulumiStack(azureStack)
    .WithResourceGroup(sharedRg)
    .ConfigureInfrastructure(infra => infra.SignalRService.Set("cors.allowedOrigins", new[] { "*" }));

var pubsub = builder.AddPulumiWebPubSub("pubsub")
    .WithPulumiStack(azureStack)
    .WithResourceGroup(sharedRg)
    .ConfigureInfrastructure(infra => infra.WebPubSubService.Set("tls.clientCertEnabled", false));
pubsub.AddHub("notifications")
    .AddEventHandler("https://example.com/api/{event}", userEventPattern: "*", systemEvents: ["connected"]);

var kusto = builder.AddPulumiKustoCluster("kusto")
    .WithPulumiStack(azureStack)
    .WithResourceGroup(sharedRg)
    .ConfigureInfrastructure(infra => infra.KustoCluster.Set("enableStreamingIngest", true));
var telemetry = kusto.AddReadWriteDatabase("telemetry");

var search = builder.AddPulumiSearch("search")
    .WithPulumiStack(azureStack)
    .WithResourceGroup(sharedRg)
    .ConfigureInfrastructure(infra => infra.SearchService.Set("semanticSearch", "free"));

builder.AddProject<Projects.AspirePulumiPackages_Api>("api")
    .WithReference(signalr)
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(acaEnv)
    .WithRoleAssignments(appConfig, AzureAppConfigurationRole.AppConfigurationDataReader)
    .WithReference(appConfig)
    .WithReference(appRegistration)
    .WithReference(cosmos)
    .WithReference(blobs)
    .WithReference(chat)
    .WithReference(pubsub)
    .WithReference(telemetry)
    .WithReference(search);

builder.Build().Run();
