using AspirePulumiPackages.AppHost.Pulumi;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("aspire-pulumi-packages", "test")
    .WithOrganization("two4suited")
    .WithEscEnvironment("azure/test");

builder.Build().Run();
