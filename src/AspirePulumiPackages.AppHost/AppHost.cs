using AspirePulumiPackages.Pulumi;
using AspirePulumiPackages.Pulumi.Azure;
using AspirePulumiPackages.Pulumi.Azure.AppContainers;
using AspirePulumiPackages.Pulumi.Azure.Base;
using AspirePulumiPackages.Pulumi.Azure.ContainerRegistry;
using AspirePulumiPackages.Pulumi.Azure.EntraId;
using AspirePulumiPackages.Pulumi.Azure.KeyVault;
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

var apiKey = builder.AddParameter("api-key", secret: true);
var keyVault = builder.AddPulumiAzureKeyVault("vault")
    .WithPulumiStack(azureStack)
    .AddSecret("api-key", apiKey);

builder.AddProject<Projects.AspirePulumiPackages_Api>("api")
    .WithReference(blobs)
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(acaEnv)
    .WithReference(appRegistration)
    .WithReference(cosmos)
    .WithRoleAssignments(keyVault, AzureKeyVaultRole.KeyVaultSecretsUser)
    .WithReference(keyVault);

builder.Build().Run();
