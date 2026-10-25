using AspirePulumiPackages.AppHost.Pulumi;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("aspire-pulumi-packages", "dev")
    .WithOrganization("two4suited");

builder.Build().Run();
