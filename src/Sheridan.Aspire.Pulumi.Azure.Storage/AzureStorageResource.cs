using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Sheridan.Aspire.Pulumi.Azure.Base;

namespace Sheridan.Aspire.Pulumi.Azure.Storage;

/// <summary>
/// An Azure Storage account provisioned by Pulumi (azure-native), mirroring Aspire.Hosting.Azure.Storage's Bicep:
/// StorageV2 / Standard_GRS / Hot / TLS1_2 / shared-key access disabled, with blob, queue and table services and
/// the containers, queues and tables added to the builder. Compute environments hosting an app that references the
/// account (or one of its blob/queue/table children) are granted the matching Storage * Data Contributor role.
/// </summary>
public sealed class AzureStorageResource(string name) : PulumiAzureResource(name)
{
    public const string BlobDataContributorRoleId = AzureRoleDefinitions.StorageBlobDataContributor;
    public const string QueueDataContributorRoleId = "974c5e8b-45b9-4653-ba55-5f855dd0fb88";
    public const string TableDataContributorRoleId = "0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3";

    /// <summary>Storage SKU (default Standard_GRS, as in the official integration).</summary>
    public string Sku { get; set; } = "Standard_GRS";

    public AzureStorageEmulatorResource? Emulator { get; set; }

    public List<AzureBlobStorageResource> BlobServices { get; } = [];
    public List<AzureQueueStorageResource> QueueServices { get; } = [];
    public List<AzureTableStorageResource> TableServices { get; } = [];
    public List<AzureBlobStorageContainerResource> BlobContainers { get; } = [];
    public List<AzureQueueStorageQueueResource> Queues { get; } = [];

    /// <summary>Every child resource added to the account (services, containers and queues).</summary>
    internal IEnumerable<IResource> Children =>
        BlobServices.Cast<IResource>().Concat(QueueServices).Concat(TableServices).Concat(BlobContainers).Concat(Queues);

    /// <summary>Publishes <paramref name="state"/> for the account and each of its children (the dashboard shows Unknown otherwise).</summary>
    internal async Task PublishStateAsync(ResourceNotificationService notifications, ResourceStateSnapshot state)
    {
        foreach (var resource in Children.Prepend(this))
            await notifications.PublishUpdateAsync(resource, s => s with { State = state }).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes every resource that references the account or one of its children wait for the account (which is only Running once
    /// it is usable). <paramref name="skip"/> are resources that belong to the account itself.
    /// </summary>
    internal void AddWaitForReferencing(DistributedApplicationModel model, IResource skip)
    {
        var related = new HashSet<IResource>(Children.Prepend(this));
        foreach (var resource in model.Resources)
        {
            if (related.Contains(resource) || resource == skip) continue;
            var references = resource.Annotations.OfType<ResourceRelationshipAnnotation>()
                .Where(a => a.Type == "Reference" && related.Contains(a.Resource));
            if (references.Any() && !resource.Annotations.OfType<WaitAnnotation>().Any(w => w.Resource == this))
                resource.Annotations.Add(new WaitAnnotation(this, WaitType.WaitUntilHealthy));
        }
    }

    internal PulumiOutputReference OutputRef(string suffix) =>
        Output(Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack()."), suffix);

    public PulumiOutputReference BlobEndpoint => OutputRef("BlobEndpoint");
    public PulumiOutputReference DataLakeEndpoint => OutputRef("DataLakeEndpoint");
    public PulumiOutputReference QueueEndpoint => OutputRef("QueueEndpoint");
    public PulumiOutputReference TableEndpoint => OutputRef("TableEndpoint");
    public PulumiOutputReference NameOutput => OutputRef("Name");

    /// <summary>Connection value of a service: the Azurite connection string when emulated, otherwise the service endpoint.</summary>
    internal ReferenceExpression BlobConnection => Emulator is { } e ? EmulatorConnection(e, "blob") : ReferenceExpression.Create($"{BlobEndpoint}");
    internal ReferenceExpression QueueConnection => Emulator is { } e ? EmulatorConnection(e, "queue") : ReferenceExpression.Create($"{QueueEndpoint}");
    internal ReferenceExpression TableConnection => Emulator is { } e ? EmulatorConnection(e, "table") : ReferenceExpression.Create($"{TableEndpoint}");

    private static ReferenceExpression EmulatorConnection(AzureStorageEmulatorResource e, string service) =>
        ReferenceExpression.Create($"DefaultEndpointsProtocol=http;AccountName={AzureStorageEmulatorResource.AccountName};AccountKey={AzureStorageEmulatorResource.AccountKey};{char.ToUpperInvariant(service[0]) + service[1..]}Endpoint={e.GetEndpoint(service)}/{AzureStorageEmulatorResource.AccountName};");

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        // Account names are global, 3-24 lowercase alphanumerics: {project}{name}{stack}, shortened with a hash when too long.
        // An infra target: role assignments are created in the targeted provision pass and depend on the account.
        program.AddInfraResource(Id, "azure-native:storage:StorageAccount", new Dictionary<string, object?>
        {
            ["type"] = "azure-native:storage:StorageAccount",
            ["properties"] = new Dictionary<string, object?>
            {
                ["accountName"] = NameFor(AzureNameKind.StorageAccount),
                ["resourceGroupName"] = rg,
                ["location"] = location,
                ["kind"] = "StorageV2",
                ["sku"] = new Dictionary<string, object?> { ["name"] = Sku },
                ["accessTier"] = "Hot",
                ["allowSharedKeyAccess"] = false,
                ["isHnsEnabled"] = false,
                ["minimumTlsVersion"] = "TLS1_2",
                ["networkRuleSet"] = new Dictionary<string, object?> { ["defaultAction"] = "Allow" },
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
            },
        });

        Dictionary<string, object?> Child(string type, Dictionary<string, object?> extra)
        {
            var props = new Dictionary<string, object?> { ["resourceGroupName"] = rg, ["accountName"] = $"${{{Id}.name}}" };
            foreach (var (k, v) in extra) props[k] = v;
            return new() { ["type"] = type, ["properties"] = props };
        }

        // The default service resources exist whenever the corresponding child is added (as in the official Bicep).
        if (BlobServices.Count > 0 || BlobContainers.Count > 0)
            program.Resources[$"{Id}BlobService"] = Child("azure-native:storage:BlobServiceProperties", new() { ["blobServicesName"] = "default" });
        if (QueueServices.Count > 0 || Queues.Count > 0)
            program.Resources[$"{Id}QueueService"] = Child("azure-native:storage:QueueServiceProperties", new() { ["queueServiceName"] = "default" });
        if (TableServices.Count > 0)
            program.Resources[$"{Id}TableService"] = Child("azure-native:storage:TableServiceProperties", new() { ["tableServiceName"] = "default" });

        foreach (var c in BlobContainers)
        {
            var d = Child("azure-native:storage:BlobContainer", new() { ["containerName"] = c.BlobContainerName });
            d["options"] = new Dictionary<string, object?> { ["dependsOn"] = new List<object?> { $"${{{Id}BlobService}}" } };
            program.Resources[$"{Id}{c.Id}"] = d;
        }

        foreach (var q in Queues)
        {
            var d = Child("azure-native:storage:Queue", new() { ["queueName"] = q.QueueName });
            d["options"] = new Dictionary<string, object?> { ["dependsOn"] = new List<object?> { $"${{{Id}QueueService}}" } };
            program.Resources[$"{Id}{q.Id}"] = d;
        }

        program.Outputs[$"{Id}BlobEndpoint"] = $"${{{Id}.primaryEndpoints.blob}}";
        program.Outputs[$"{Id}DataLakeEndpoint"] = $"${{{Id}.primaryEndpoints.dfs}}";
        program.Outputs[$"{Id}QueueEndpoint"] = $"${{{Id}.primaryEndpoints.queue}}";
        program.Outputs[$"{Id}TableEndpoint"] = $"${{{Id}.primaryEndpoints.table}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";

        AddRoleAssignments(program, context);
        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }

    /// <summary>Grants each compute environment hosting a referencing app the data-plane roles for the referenced services.</summary>
    private void AddRoleAssignments(PulumiProgram program, PipelineStepContext ctx)
    {
        var children = new List<(IResource Resource, string RoleId, string RoleName)>();
        children.AddRange(BlobServices.Select(s => ((IResource)s, BlobDataContributorRoleId, "Blob")));
        children.AddRange(BlobContainers.Select(s => ((IResource)s, BlobDataContributorRoleId, "Blob")));
        children.AddRange(QueueServices.Select(s => ((IResource)s, QueueDataContributorRoleId, "Queue")));
        children.AddRange(Queues.Select(s => ((IResource)s, QueueDataContributorRoleId, "Queue")));
        children.AddRange(TableServices.Select(s => ((IResource)s, TableDataContributorRoleId, "Table")));

        var wanted = new Dictionary<(IPulumiManagedIdentityResource Env, string Role), string>();
        foreach (var app in ctx.Model.GetComputeResources())
        {
            var referenced = app.Annotations.OfType<ResourceRelationshipAnnotation>().Select(a => a.Resource).ToList();
            var envs = app.Annotations.OfType<DeploymentTargetAnnotation>()
                .Select(a => a.ComputeEnvironment).OfType<IPulumiManagedIdentityResource>();
            foreach (var env in envs)
            {
                if (referenced.Contains(this))
                {
                    // Referencing the account itself grants all three data-plane roles.
                    wanted[(env, "Blob")] = BlobDataContributorRoleId;
                    wanted[(env, "Queue")] = QueueDataContributorRoleId;
                    wanted[(env, "Table")] = TableDataContributorRoleId;
                }
                foreach (var (res, roleId, roleName) in children)
                    if (referenced.Contains(res)) wanted[(env, roleName)] = roleId;
            }
        }

        foreach (var ((env, roleName), roleId) in wanted)
        {
            var envId = PulumiAzureProgramExtensions.ToPulumiId(env.Name);
            program.AddRoleAssignment($"{Id}{envId}{roleName}RoleAssignment", env.PrincipalIdExpression, roleId, $"${{{Id}.id}}");
        }
    }
}

/// <summary>Azurite, run locally in place of the Pulumi-provisioned account.</summary>
public sealed class AzureStorageEmulatorResource(AzureStorageResource innerResource) : ContainerResource(innerResource.Name + "-emulator")
{
    /// <summary>Azurite's fixed, publicly documented development account.</summary>
    public const string AccountName = "devstoreaccount1";
    public const string AccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    public AzureStorageResource InnerResource { get; } = innerResource;
}

/// <summary>Blob service of an <see cref="AzureStorageResource"/> (connection string = blob endpoint).</summary>
public sealed class AzureBlobStorageResource(string name, AzureStorageResource parent) : Resource(name), IResourceWithConnectionString, IResourceWithParent<AzureStorageResource>
{
    public AzureStorageResource Parent { get; } = parent;

    public ReferenceExpression ConnectionStringExpression => Parent.BlobConnection;
}

/// <summary>Queue service of an <see cref="AzureStorageResource"/> (connection string = queue endpoint).</summary>
public sealed class AzureQueueStorageResource(string name, AzureStorageResource parent) : Resource(name), IResourceWithConnectionString, IResourceWithParent<AzureStorageResource>
{
    public AzureStorageResource Parent { get; } = parent;

    public ReferenceExpression ConnectionStringExpression => Parent.QueueConnection;
}

/// <summary>Table service of an <see cref="AzureStorageResource"/> (connection string = table endpoint).</summary>
public sealed class AzureTableStorageResource(string name, AzureStorageResource parent) : Resource(name), IResourceWithConnectionString, IResourceWithParent<AzureStorageResource>
{
    public AzureStorageResource Parent { get; } = parent;

    public ReferenceExpression ConnectionStringExpression => Parent.TableConnection;
}

/// <summary>A blob container in the account (connection string = <c>Endpoint=...;ContainerName=...</c>).</summary>
public sealed class AzureBlobStorageContainerResource(string name, string blobContainerName, AzureStorageResource parent) : Resource(name), IResourceWithConnectionString, IResourceWithParent<AzureStorageResource>
{
    public AzureStorageResource Parent { get; } = parent;
    public string BlobContainerName { get; } = blobContainerName;
    internal string Id => PulumiAzureProgramExtensions.ToPulumiId(Name);

    public ReferenceExpression ConnectionStringExpression => Parent.Emulator is { } e
        ? ReferenceExpression.Create($"Endpoint={e.GetEndpoint("blob")}/{AzureStorageEmulatorResource.AccountName};ContainerName={BlobContainerName};")
        : ReferenceExpression.Create($"Endpoint={Parent.BlobEndpoint};ContainerName={BlobContainerName};");
}

/// <summary>A queue in the account (connection string = <c>Endpoint=...;QueueName=...</c>).</summary>
public sealed class AzureQueueStorageQueueResource(string name, string queueName, AzureStorageResource parent) : Resource(name), IResourceWithConnectionString, IResourceWithParent<AzureStorageResource>
{
    public AzureStorageResource Parent { get; } = parent;
    public string QueueName { get; } = queueName;
    internal string Id => PulumiAzureProgramExtensions.ToPulumiId(Name);

    public ReferenceExpression ConnectionStringExpression => Parent.Emulator is { } e
        ? ReferenceExpression.Create($"Endpoint={e.GetEndpoint("queue")}/{AzureStorageEmulatorResource.AccountName};QueueName={QueueName};")
        : ReferenceExpression.Create($"Endpoint={Parent.QueueEndpoint};QueueName={QueueName};");
}
