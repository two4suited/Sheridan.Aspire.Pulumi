using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using AspirePulumiPackages.Pulumi.Azure.Base;
using AspirePulumiPackages.Pulumi.Azure.Storage;

namespace AspirePulumiPackages.Pulumi.Azure.Functions;

/// <summary>Pulumi equivalents of the Aspire.Hosting.Azure.Functions extension methods.</summary>
public static class AzureFunctionsProjectResourceExtensions
{
    /// <summary>Prefix of the implicit host storage account name, as in the official integration.</summary>
    internal const string DefaultHostStorageName = "funcstorage";

    /// <summary>The container port Functions listens on when deployed (the .NET image default, not the Functions image's 80).</summary>
    private const int DeployedPort = 8080;

    private sealed class FunctionsProjectMetadata(string projectPath) : IProjectMetadata
    {
        private string? _resolved;

        public string ProjectPath => _resolved ??= Resolve(projectPath);

        public bool SuppressBuild => false;

        private static string Resolve(string path)
        {
            if (!Directory.Exists(path)) return path;
            var files = Directory.GetFiles(path, "*.csproj", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true });
            return files.Length == 1 ? Path.GetFullPath(files[0]) : path;
        }
    }

    /// <summary>
    /// Adds an Azure Functions project (the equivalent of AddAzureFunctionsProject). By default an implicit Pulumi-provisioned
    /// storage account (<c>funcstorage&lt;hash&gt;</c>, Azurite in run mode) is the Functions host storage; use
    /// <see cref="WithHostStorage"/> to choose another. Deployed to Container Apps, the app has <c>kind: functionapp</c>.
    /// </summary>
    public static IResourceBuilder<AzureFunctionsProjectResource> AddPulumiAzureFunctionsProject<TProject>(
        this IDistributedApplicationBuilder builder, string name) where TProject : IProjectMetadata, new() =>
        AddCore(builder, new AzureFunctionsProjectResource(name), new TProject());

    /// <summary>Adds an Azure Functions project by project file (or directory) path, relative to the AppHost directory.</summary>
    public static IResourceBuilder<AzureFunctionsProjectResource> AddPulumiAzureFunctionsProject(
        this IDistributedApplicationBuilder builder, string name, string projectPath)
    {
        projectPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, projectPath)
            .Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
        return AddCore(builder, new AzureFunctionsProjectResource(name), new FunctionsProjectMetadata(projectPath));
    }

    /// <summary>Alias of <see cref="AddPulumiAzureFunctionsProject{TProject}"/>.</summary>
    public static IResourceBuilder<AzureFunctionsProjectResource> AddPulumiFunctionsProject<TProject>(
        this IDistributedApplicationBuilder builder, string name) where TProject : IProjectMetadata, new() =>
        builder.AddPulumiAzureFunctionsProject<TProject>(name);

    /// <summary>Alias of <see cref="AddPulumiAzureFunctionsProject(IDistributedApplicationBuilder, string, string)"/>.</summary>
    public static IResourceBuilder<AzureFunctionsProjectResource> AddPulumiFunctionsProject(
        this IDistributedApplicationBuilder builder, string name, string projectPath) =>
        builder.AddPulumiAzureFunctionsProject(name, projectPath);

    private static IResourceBuilder<AzureFunctionsProjectResource> AddCore(
        IDistributedApplicationBuilder builder, AzureFunctionsProjectResource resource, IProjectMetadata metadata)
    {
        var hash = builder.Configuration["AppHost:ProjectNameSha256"];
        var storageName = DefaultHostStorageName + (string.IsNullOrEmpty(hash) ? "" : hash[..Math.Min(5, hash.Length)].ToLowerInvariant());
        var storage = builder.Resources.OfType<AzureStorageResource>().FirstOrDefault(r => r.Name == storageName)
            ?? builder.AddPulumiStorage(storageName).RunAsEmulator().Resource;
        resource.HostStorage = storage;

        builder.Eventing.Subscribe<BeforeStartEvent>((e, _) =>
        {
            var implicitStorageUsed = false;
            foreach (var fn in e.Model.Resources.OfType<AzureFunctionsProjectResource>())
            {
                if (fn.HostStorage == storage) implicitStorageUsed = true;
                // Referencing the account is what makes the Storage integration grant the compute identity the data roles.
                if (fn.HostStorage is not null) builder.CreateResourceBuilder(fn).WithReferenceRelationship(fn.HostStorage);
            }

            if (!implicitStorageUsed)
            {
                e.Model.Resources.Remove(storage);
                if (storage.Emulator is { } emulator) e.Model.Resources.Remove(emulator);
            }
            else if (storage.Stack is null)
            {
                // The implicit account lives in the (single) stack of the model unless WithPulumiStack was called on the project.
                storage.Stack = e.Model.Resources.OfType<PulumiStackResource>().SingleOrDefault();
            }

            if (implicitStorageUsed && !storage.NameOverrides.ContainsKey((AzureNameKind.StorageAccount, null)))
            {
                // Name the implicit host storage after the app instead of the internal 'funcstorage<hash>' resource name:
                // {project}{functions project | 'functions'}host{stack}. The Pulumi/Aspire resource name (its logical id) is unchanged.
                var users = e.Model.Resources.OfType<AzureFunctionsProjectResource>().Where(f => f.HostStorage == storage).ToList();
                var s = storage.Stack;
                if (s is not null)
                    storage.NameOverrides[(AzureNameKind.StorageAccount, null)] =
                        AzureNaming.Compose(AzureNameKind.StorageAccount, s.ProjectName, users.Count == 1 ? users[0].Name : "functions", s.StackName, "host");
            }
            return Task.CompletedTask;
        });

        var fn = builder.AddResource(resource)
            .WithAnnotation<IProjectMetadata>(metadata)
            .WithAnnotation(new AzureFunctionsAnnotation())
            // Container app: kind=functionapp (KEDA rules follow the triggers), HTTP ingress, scale without the 1/1 cap.
            .WithAnnotation(new PulumiContainerAppCustomizationAnnotation(ConfigureContainerApp))
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables["OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY"] = "in_memory";
                context.EnvironmentVariables["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true";
                context.EnvironmentVariables["FUNCTIONS_WORKER_RUNTIME"] = "dotnet-isolated";
                context.EnvironmentVariables["AzureFunctionsJobHost__telemetryMode"] = "OpenTelemetry";
                if (context.ExecutionContext.IsPublishMode)
                    context.EnvironmentVariables["ASPNETCORE_URLS"] = $"http://+:{DeployedPort}";
                if (resource.HostStorage is { } host)
                    ApplyConfiguration(host, context.EnvironmentVariables, "AzureWebJobsStorage");
            })
            .WithOtlpExporter();

        if (builder.ExecutionContext.IsRunMode)
            fn.WithRequiredCommand("func", "https://learn.microsoft.com/azure/azure-functions/functions-run-local#install-the-azure-functions-core-tools");

        return fn.WithFunctionsHttpEndpoint();
    }

    private static IResourceBuilder<AzureFunctionsProjectResource> WithFunctionsHttpEndpoint(this IResourceBuilder<AzureFunctionsProjectResource> builder)
    {
        if (builder.ApplicationBuilder.ExecutionContext.IsPublishMode)
            return builder.WithHttpEndpoint(targetPort: DeployedPort);

        // Run mode: the host picks a free port and passes it to `func host start` as --port.
        return builder.WithHttpEndpoint(isProxied: true).WithArgs(context =>
        {
            var endpoint = builder.Resource.GetEndpoint("http");
            context.Args.Add("--port");
            context.Args.Add(endpoint.Property(EndpointProperty.TargetPort));
        });
    }

    /// <summary>Sets <c>kind: functionapp</c>, <c>http</c> ingress transport and removes the default 1/1 replica cap.</summary>
    private static void ConfigureContainerApp(Dictionary<string, object?> app)
    {
        if (app["properties"] is not Dictionary<string, object?> props) return;
        props["kind"] = "functionapp";
        if (props.TryGetValue("configuration", out var cfg) && cfg is Dictionary<string, object?> c
            && c.TryGetValue("ingress", out var ing) && ing is Dictionary<string, object?> ingress)
            ingress["transport"] = "http";
        if (props.TryGetValue("template", out var tpl) && tpl is Dictionary<string, object?> t
            && t.TryGetValue("scale", out var sc) && sc is Dictionary<string, object?> scale)
            scale.Remove("maxReplicas");
    }

    /// <summary>
    /// Uses <paramref name="storage"/> as the Functions host storage instead of the implicit account (the equivalent of WithHostStorage).
    /// The account must be bound to a Pulumi stack (<c>WithPulumiStack</c>) when the model has more than one.
    /// </summary>
    public static IResourceBuilder<AzureFunctionsProjectResource> WithHostStorage(
        this IResourceBuilder<AzureFunctionsProjectResource> builder, IResourceBuilder<AzureStorageResource> storage)
    {
        builder.Resource.HostStorage = storage.Resource;
        return builder;
    }

    /// <summary>
    /// Binds the implicit host storage account to a Pulumi stack (use <c>WithPulumiStack</c> on the storage itself when
    /// <see cref="WithHostStorage"/> supplies one).
    /// </summary>
    public static IResourceBuilder<AzureFunctionsProjectResource> WithPulumiStack(
        this IResourceBuilder<AzureFunctionsProjectResource> builder, IResourceBuilder<PulumiStackResource> stack)
    {
        if (builder.Resource.HostStorage is { Stack: null } host) host.Stack = stack.Resource;
        return builder;
    }

    internal static IResourceBuilder<AzureFunctionsProjectResource>? TryWithReference(
        IResourceBuilder<AzureFunctionsProjectResource> destination, IResourceBuilder<IResource> source, string? connectionName, bool optional, string? name)
    {
        if (source.Resource is not (AzureStorageResource or AzureBlobStorageResource or AzureQueueStorageResource or AzureTableStorageResource))
            return null;
        if (optional) throw new InvalidOperationException("Optional references are not supported for Azure Functions resources.");
        if (name is not null) throw new InvalidOperationException("Named service references are not supported for Azure Functions resources.");

        // The default ConnectionStrings__{name} key conflicts with Functions' expectation that single-valued ConnectionStrings
        // entries are connection strings, so the settings are injected as {name}__blobServiceUri etc. (+ Aspire client keys).
        destination.WithReferenceRelationship(source.Resource);
        return destination.WithEnvironment(context => ApplyConfiguration(source.Resource, context.EnvironmentVariables, connectionName ?? source.Resource.Name));
    }

    /// <summary>The Pulumi counterpart of IResourceWithAzureFunctionsConfig.ApplyAzureFunctionsConfiguration for the Storage resources.</summary>
    private static void ApplyConfiguration(IResource source, IDictionary<string, object> target, string connectionName)
    {
        switch (source)
        {
            case AzureStorageResource { Emulator: not null } account:
                SetEmulator(target, account, connectionName, blobs: true, queues: true, tables: true);
                break;
            case AzureStorageResource account:
                target[connectionName + "__blobServiceUri"] = account.BlobEndpoint;
                target[connectionName + "__queueServiceUri"] = account.QueueEndpoint;
                target[connectionName + "__tableServiceUri"] = account.TableEndpoint;
                target[connectionName + "__dataLakeServiceUri"] = account.DataLakeEndpoint;
                target[$"Aspire__Azure__Storage__Blobs__{connectionName}__ServiceUri"] = account.BlobEndpoint;
                target[$"Aspire__Azure__Storage__Queues__{connectionName}__ServiceUri"] = account.QueueEndpoint;
                target[$"Aspire__Azure__Data__Tables__{connectionName}__ServiceUri"] = account.TableEndpoint;
                target[$"Aspire__Azure__Storage__Files__DataLake__{connectionName}__ServiceUri"] = account.DataLakeEndpoint;
                break;
            case AzureBlobStorageResource { Parent.Emulator: not null } blobs:
                SetEmulator(target, blobs.Parent, connectionName, blobs: true, queues: false, tables: false);
                break;
            case AzureBlobStorageResource blobs:
                target[connectionName + "__blobServiceUri"] = blobs.Parent.BlobEndpoint;
                target[connectionName + "__queueServiceUri"] = blobs.Parent.QueueEndpoint;
                target[$"Aspire__Azure__Storage__Blobs__{connectionName}__ServiceUri"] = blobs.Parent.BlobEndpoint;
                break;
            case AzureQueueStorageResource { Parent.Emulator: not null } queues:
                SetEmulator(target, queues.Parent, connectionName, blobs: false, queues: true, tables: false);
                break;
            case AzureQueueStorageResource queues:
                target[connectionName + "__queueServiceUri"] = queues.Parent.QueueEndpoint;
                target[$"Aspire__Azure__Storage__Queues__{connectionName}__ServiceUri"] = queues.Parent.QueueEndpoint;
                break;
            case AzureTableStorageResource { Parent.Emulator: not null } tables:
                SetEmulator(target, tables.Parent, connectionName, blobs: false, queues: false, tables: true);
                break;
            case AzureTableStorageResource tables:
                target[connectionName + "__tableServiceUri"] = tables.Parent.TableEndpoint;
                target[$"Aspire__Azure__Data__Tables__{connectionName}__ServiceUri"] = tables.Parent.TableEndpoint;
                break;
        }
    }

    private static void SetEmulator(IDictionary<string, object> target, AzureStorageResource account, string connectionName, bool blobs, bool queues, bool tables)
    {
        var e = account.Emulator!;
        const string acct = AzureStorageEmulatorResource.AccountName;
        var value = ReferenceExpression.Create(
            $"DefaultEndpointsProtocol=http;AccountName={acct};AccountKey={AzureStorageEmulatorResource.AccountKey};BlobEndpoint={e.GetEndpoint("blob")}/{acct};QueueEndpoint={e.GetEndpoint("queue")}/{acct};TableEndpoint={e.GetEndpoint("table")}/{acct};");
        target[connectionName] = value;
        if (blobs) target[$"Aspire__Azure__Storage__Blobs__{connectionName}__ConnectionString"] = value;
        if (queues) target[$"Aspire__Azure__Storage__Queues__{connectionName}__ConnectionString"] = value;
        if (tables) target[$"Aspire__Azure__Data__Tables__{connectionName}__ConnectionString"] = value;
    }
}
