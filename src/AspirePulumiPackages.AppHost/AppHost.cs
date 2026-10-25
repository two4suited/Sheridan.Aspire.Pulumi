using AspirePulumiPackages.Pulumi;
using AspirePulumiPackages.Pulumi.Azure;
using AspirePulumiPackages.Pulumi.Entra;

var builder = DistributedApplication.CreateBuilder(args);

var appName = "aspire-pulumi-packages";

var entraStack = builder.AddPulumiStack($"{appName}-entra", "entra-test")
    .WithProjectName(appName)
    .WithOrganization("two4suited")
    .WithEscEnvironment("entra/test");

var appRegistration = builder.AddPulumiAzureAdAppRegistration("app-registration")
    .WithPulumiStack(entraStack);

var azureStack = builder.AddPulumiStack(appName, "azure-test")
    .WithProjectName(appName)
    .WithOrganization("two4suited")
    .WithEscEnvironment("azure/test")
    .DependsOnStack(entraStack);

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env")
    .WithPulumiStack(azureStack);

var cosmos = builder.AddPulumiAzureCosmosDB("cosmos")
    .WithPulumiStack(azureStack)
    .RunAsEmulator();
cosmos.AddCosmosDatabase("appdb").AddContainer("items", "/id");

builder.AddProject<Projects.AspirePulumiPackages_Api>("api")
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(acaEnv)
    .WithEnvironment("AzureAd__ClientId", appRegistration.Resource.ClientId)
    .WithReference(cosmos);

builder.Build().Run();
