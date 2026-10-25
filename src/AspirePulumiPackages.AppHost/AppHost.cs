using AspirePulumiPackages.Pulumi;
using AspirePulumiPackages.Pulumi.Azure;
using AspirePulumiPackages.Pulumi.Azure.Base;
using AspirePulumiPackages.Pulumi.Azure.ContainerRegistry;
using AspirePulumiPackages.Pulumi.Azure.EntraId;

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

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env")
    .WithPulumiStack(azureStack);

var acr = builder.AddPulumiAzureContainerRegistry("acr")
    .WithPulumiStack(azureStack)
    .WithPurgeTask("0 1 * * *", ago: TimeSpan.FromDays(7), keep: 5);
acaEnv.WithRoleAssignments(acr, AzureContainerRegistryRole.AcrPull);

var cosmos =builder.AddPulumiAzureCosmosDB("cosmos")
    .WithPulumiStack(azureStack)
    .RunAsEmulator();
cosmos.AddCosmosDatabase("appdb").AddContainer("items", "/id");

builder.AddProject<Projects.AspirePulumiPackages_Api>("api")
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(acaEnv)
    .WithReference(appRegistration)
    .WithReference(cosmos);

builder.Build().Run();
