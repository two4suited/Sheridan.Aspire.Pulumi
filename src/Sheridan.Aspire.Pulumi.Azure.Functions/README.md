# Sheridan.Aspire.Pulumi.Azure.Functions

Azure Functions projects for Aspire, deployed to a Pulumi-provisioned compute environment.

`AddPulumiAzureFunctionsProject` mirrors `AddAzureFunctionsProject`: a project resource that sets the Functions environment variables and wires identity-based host storage (`AzureWebJobsStorage__blobServiceUri`, ...).

## Install

```bash
dotnet add package Sheridan.Aspire.Pulumi.Azure.Functions
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPulumiStack("my-app", "dev").WithOrganization("my-org");

var storage = builder.AddPulumiStorage("storage").RunAsEmulator();   // Sheridan.Aspire.Pulumi.Azure.Storage
var blobs = storage.AddBlobs("blobs");
var acaEnv = builder.AddPulumiAzureContainerAppEnvironment("aca-env");   // Sheridan.Aspire.Pulumi.Azure.AppContainers

builder.AddPulumiAzureFunctionsProject("func", "../MyFunctions/MyFunctions.csproj")
    // .WithHostStorage(storage)   // use your own storage account instead of the implicit one
    .WithReference(blobs)
    .WithComputeEnvironment(acaEnv);

builder.Build().Run();
```

## Features

- By default an implicit `funcstorage<hash>` storage account is created (Azurite in run mode); `WithHostStorage(storage)` replaces it.
- On Container Apps the app is deployed with `kind: functionapp`, `http` ingress and KEDA scale rules that follow the triggers.
- The environment identity is granted Storage Blob/Queue/Table Data Contributor plus Storage Account Contributor on the host storage, matching the official Bicep.

## Notes

- Not ported: launch-profile port discovery in run mode, `AZURE_CLIENT_ID` injection and the Durable Task scheduler APIs.
- Storage endpoints are read from stack outputs, which the first `aspire deploy` creates in its provision pass.

## Learn more

- Mirrors the official [`Aspire.Hosting.Azure.Functions`](https://www.nuget.org/packages/Aspire.Hosting.Azure.Functions) integration, so moving from Bicep to Pulumi is a drop-in change in your AppHost.
- Stack, ESC and deploy setup: [repository README](https://github.com/two4suited/Sheridan.Aspire.Pulumi#readme).
- Customizing generated resources with `ConfigureInfrastructure`: [provisioning guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/provisioning.md).
- Resource naming: [naming guide](https://github.com/two4suited/Sheridan.Aspire.Pulumi/blob/main/docs/naming.md).
