# Sheridan.Aspire.Pulumi.Azure.Functions

_Azure Functions_ — part of [Sheridan.Aspire.Pulumi](../../README.md).

`Sheridan.Aspire.Pulumi.Azure.Functions` ports `Aspire.Hosting.Azure.Functions`. `builder.AddPulumiAzureFunctionsProject("func", "../MyFunctions/MyFunctions.csproj")` (or `<TProject>`; alias `AddPulumiFunctionsProject`) mirrors `AddAzureFunctionsProject`: a `ProjectResource` that sets the Functions environment variables (`FUNCTIONS_WORKER_RUNTIME`, OpenTelemetry mode, `ASPNETCORE_URLS`) and wires host storage as `AzureWebJobsStorage__{blob,queue,table,dataLake}ServiceUri` (identity-based).

```csharp
var storage = builder.AddPulumiStorage("storage").WithPulumiStack(azureStack).RunAsEmulator();
var blobs = storage.AddBlobs("blobs");

builder.AddPulumiAzureFunctionsProject("func", "../MyFunctions/MyFunctions.csproj")
    .WithPulumiStack(azureStack)      // binds the implicit host storage account
    // .WithHostStorage(storage)      // or use your own Pulumi storage account instead of the implicit one
    .WithReference(blobs)             // injects blobs__blobServiceUri etc. (not ConnectionStrings__blobs), as upstream
    .WithComputeEnvironment(acaEnv);
```

By default an implicit `funcstorage<hash>` storage account is created (Azurite in run mode; dropped from the model if every project uses `WithHostStorage`). Deployed through `AddPulumiAzureContainerAppEnvironment`, the container app gets `kind: functionapp` (KEDA scale rules follow the triggers), `http` ingress and no 1-replica cap; the environment identity is granted Storage Blob/Queue/Table Data Contributor (via the Storage integration) plus Storage Account Contributor on the host storage, matching the official Bicep. The compute environment hook is `PulumiContainerAppCustomizationAnnotation` in `Sheridan.Aspire.Pulumi.Azure`. Not ported: launch-profile port discovery in run mode, `AZURE_CLIENT_ID` injection (the shared environment identity is not exposed with a client id yet), the Durable Task scheduler APIs. Like the other integrations, storage endpoints are read from stack outputs, which the first `aspire deploy` creates in its provision pass.
