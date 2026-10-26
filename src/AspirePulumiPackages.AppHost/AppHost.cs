using AspirePulumiPackages.Pulumi;
using AspirePulumiPackages.Pulumi.Azure;
using AspirePulumiPackages.Pulumi.Azure.AppContainers;
using AspirePulumiPackages.Pulumi.Azure.Base;
using AspirePulumiPackages.Pulumi.Azure.EntraId;
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

var pubsub = builder.AddPulumiWebPubSub("pubsub")
    .WithPulumiStack(azureStack)
    .WithResourceGroup(sharedRg)
    .ConfigureInfrastructure(infra => infra.WebPubSubService.Set("tls.clientCertEnabled", false));
pubsub.AddHub("notifications")
    .AddEventHandler("https://example.com/api/{event}", userEventPattern: "*", systemEvents: ["connected"]);

builder.AddProject<Projects.AspirePulumiPackages_Api>("api")
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(acaEnv)
    .WithReference(appRegistration)
    .WithReference(cosmos)
    .WithReference(blobs)
    .WithReference(pubsub);

builder.Build().Run();
