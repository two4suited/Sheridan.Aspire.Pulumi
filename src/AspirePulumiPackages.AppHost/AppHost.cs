using AspirePulumiPackages.Pulumi;
using AspirePulumiPackages.Pulumi.Azure;
using AspirePulumiPackages.Pulumi.Azure.Base;
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

var sharedRg = builder.AddPulumiAzureResourceGroup("shared-rg")
    .WithPulumiStack(azureStack);

var workloadIdentity = builder.AddPulumiAzureUserAssignedIdentity("workload-identity")
    .WithPulumiStack(azureStack)
    .WithResourceGroup(sharedRg);

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env")
    .WithPulumiStack(azureStack);

var cosmos = builder.AddPulumiAzureCosmosDB("cosmos")
    .WithPulumiStack(azureStack)
    .RunAsEmulator();
cosmos.AddCosmosDatabase("appdb").AddContainer("items", "/id");

builder.AddProject<Projects.AspirePulumiPackages_Api>("api")
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(acaEnv)
    .WithReference(appRegistration)
    .WithReference(cosmos)
    .WithEnvironment("WorkloadIdentity__ClientId", workloadIdentity.Resource.ClientId);

builder.Build().Run();
