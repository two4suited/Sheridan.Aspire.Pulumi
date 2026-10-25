using AspirePulumiPackages.AppHost.Pulumi;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("aspire-pulumi-packages", "test")
    .WithOrganization("two4suited")
    .WithEscEnvironment("azure/test");

var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");

builder.AddProject<Projects.AspirePulumiPackages_Api>("api")
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(acaEnv);

builder.Build().Run();
