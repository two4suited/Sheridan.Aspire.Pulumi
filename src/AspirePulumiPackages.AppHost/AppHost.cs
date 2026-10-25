using AspirePulumiPackages.AppHost.Pulumi;

var builder = DistributedApplication.CreateBuilder(args);

var entraStack = builder.AddPulumiStack("entra-app", "test")
    .WithOrganization("two4suited")
    .WithEscEnvironment("entra/test");

var appRegistration = builder.AddPulumiAzureAdAppRegistration("app-registration")
    .WithPulumiStack(entraStack);

var azureStack = builder.AddPulumiStack("aspire-pulumi-packages", "test")
    .WithOrganization("two4suited")
    .WithEscEnvironment("azure/test")
    .DependsOnStack(entraStack);

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env")
    .WithPulumiStack(azureStack);

builder.AddProject<Projects.AspirePulumiPackages_Api>("api")
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(acaEnv)
    .WithEnvironment("AzureAd__ClientId", appRegistration.Resource.ClientId);

builder.Build().Run();
