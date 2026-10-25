using AspirePulumiPackages.Pulumi;
using AspirePulumiPackages.Pulumi.Azure;
using AspirePulumiPackages.Pulumi.Azure.AppContainers;
using AspirePulumiPackages.Pulumi.Azure.ApplicationInsights;
using AspirePulumiPackages.Pulumi.Azure.Base;
using AspirePulumiPackages.Pulumi.Azure.ContainerRegistry;
using AspirePulumiPackages.Pulumi.Azure.EntraId;
using AspirePulumiPackages.Pulumi.Azure.EventHubs;
using AspirePulumiPackages.Pulumi.Azure.KeyVault;
using AspirePulumiPackages.Pulumi.Azure.ServiceBus;
using AspirePulumiPackages.Pulumi.Azure.Storage;

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

var workloadIdentity = builder.AddPulumiAzureUserAssignedIdentity("workload-identity")
    .WithPulumiStack(azureStack)
    .WithResourceGroup(sharedRg);

var acr = builder.AddPulumiAzureContainerRegistry("acr")
    .WithPulumiStack(azureStack)
    .WithPurgeTask("0 1 * * *", ago: TimeSpan.FromDays(7), keep: 5);

// The environment pulls from (and the API image is pushed to) the shared registry; it is granted AcrPull on it.
var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env")
    .WithPulumiStack(azureStack)
    .WithAzureContainerRegistry(acr);

var cosmos =builder.AddPulumiAzureCosmosDB("cosmos")
    .WithPulumiStack(azureStack)
    .RunAsEmulator();
cosmos.AddCosmosDatabase("appdb").AddContainer("items", "/id");

var storage = builder.AddPulumiStorage("storage")
    .WithPulumiStack(azureStack)
    .RunAsEmulator();
var blobs = storage.AddBlobs("blobs");
blobs.AddBlobContainer("uploads");
storage.AddQueues("queues").AddQueue("jobs");

var serviceBus = builder.AddPulumiServiceBus("messaging")
    .WithPulumiStack(azureStack);
serviceBus.AddServiceBusQueue("sb-orders", "orders").WithProperties(q => { q.MaxDeliveryCount = 5; q.DeadLetteringOnMessageExpiration = true; });
serviceBus.AddServiceBusTopic("events").AddServiceBusSubscription("audit")
    .WithProperties(s => s.Rules.Add(new AzureServiceBusRule("audit-only") { FilterType = AzureServiceBusFilterType.CorrelationFilter, CorrelationFilter = { Subject = "audit" } }));

var eventHubs = builder.AddPulumiEventHubs("eventhubs")
    .WithPulumiStack(azureStack);
var ordersHub = eventHubs.AddHub("orders", "orders-hub");
ordersHub.AddConsumerGroup("orders-processor", "processor");

var apiKey =builder.AddParameter("api-key", secret: true);
var keyVault = builder.AddPulumiAzureKeyVault("vault")
    .WithPulumiStack(azureStack)
    .AddSecret("api-key", apiKey)
    .ConfigureInfrastructure(infra => infra.KeyVaultService
        .Set("properties.enableSoftDelete", true)
        .Set("properties.enablePurgeProtection", true)
        .Set("properties.softDeleteRetentionInDays", 30));

var appInsights = builder.AddPulumiApplicationInsights("appinsights")
    .WithPulumiStack(azureStack);

builder.AddProject<Projects.AspirePulumiPackages_Api>("api")
    .WithReference(blobs)
    .WithReference(ordersHub)
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(acaEnv)
    .WithReference(appRegistration)
    .WithReference(cosmos)
    .WithEnvironment("WorkloadIdentity__ClientId", workloadIdentity.Resource.ClientId)
    .WithRoleAssignments(keyVault, AzureKeyVaultRole.KeyVaultSecretsUser)
    .WithReference(keyVault)
    .WithReference(serviceBus)
    .WithReference(appInsights);

builder.Build().Run();
