using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Sheridan.Aspire.Pulumi.Azure.Base;

namespace Sheridan.Aspire.Pulumi.Azure.Kusto;

/// <summary>
/// An Azure Data Explorer (Kusto) cluster provisioned by Pulumi (azure-native), mirroring Aspire.Hosting.Azure.Kusto's Bicep:
/// a Standard_E2a_v4 (capacity 2) cluster plus the read-write databases added to the builder. Compute environments hosting an
/// app that references the cluster (or one of its databases) are granted the database <c>User</c> principal role on every
/// database, as the official integration does.
/// </summary>
public sealed class AzureKustoClusterResource(string name) : PulumiAzureResource(name), IResourceWithConnectionString
{
    /// <summary>SKU name (default Standard_E2a_v4, as in the official integration).</summary>
    public string SkuName { get; set; } = "Standard_E2a_v4";
    /// <summary>SKU tier (default Standard).</summary>
    public string SkuTier { get; set; } = "Standard";
    /// <summary>Number of instances (default 2).</summary>
    public int SkuCapacity { get; set; } = 2;

    public List<AzureKustoReadWriteDatabaseResource> Databases { get; } = [];

    internal PulumiOutputReference OutputRef(string suffix) =>
        Output(Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack()."), suffix);

    /// <summary>The cluster URI, e.g. <c>https://cluster.region.kusto.windows.net</c>.</summary>
    public PulumiOutputReference ClusterUri => OutputRef("ClusterUri");
    public PulumiOutputReference NameOutput => OutputRef("Name");

    /// <summary>The cluster URI expression (the official <c>UriExpression</c>).</summary>
    public ReferenceExpression UriExpression => ReferenceExpression.Create($"{ClusterUri}");

    public ReferenceExpression ConnectionStringExpression => ReferenceExpression.Create($"Data Source={ClusterUri}");

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        program.Resources[Id] = new Dictionary<string, object?>
        {
            ["type"] = "azure-native:kusto:Cluster",
            ["properties"] = new Dictionary<string, object?>
            {
                ["clusterName"] = NameFor(AzureNameKind.KustoCluster),
                ["resourceGroupName"] = rg,
                ["location"] = location,
                ["sku"] = new Dictionary<string, object?>
                {
                    ["name"] = SkuName,
                    ["tier"] = SkuTier,
                    ["capacity"] = SkuCapacity,
                },
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
            },
        };

        foreach (var db in Databases)
        {
            program.Resources[$"{Id}{db.Id}"] = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:kusto:ReadWriteDatabase",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["location"] = location,
                    ["clusterName"] = $"${{{Id}.name}}",
                    ["databaseName"] = db.DatabaseName,
                    ["kind"] = "ReadWrite",
                },
            };

            // Not part of the official Bicep (it runs the script at runtime); provisioned here so WithCreationScript has an effect in Azure.
            if (db.CreationScript is { } script)
            {
                program.Resources[$"{Id}{db.Id}Script"] = new Dictionary<string, object?>
                {
                    ["type"] = "azure-native:kusto:Script",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["resourceGroupName"] = rg,
                        ["clusterName"] = $"${{{Id}.name}}",
                        ["databaseName"] = db.DatabaseName,
                        ["scriptName"] = $"{db.Name}-creation",
                        ["scriptContent"] = script,
                    },
                    ["options"] = new Dictionary<string, object?> { ["dependsOn"] = new List<object?> { $"${{{Id}{db.Id}}}" } },
                };
            }
        }

        program.Outputs[$"{Id}ClusterUri"] = $"${{{Id}.uri}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";

        AddRoleAssignments(program, context);
        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }

    private bool IsReferencedBy(IResource consumer)
    {
        var referenced = consumer.Annotations.OfType<ResourceRelationshipAnnotation>().Select(a => a.Resource).ToList();
        return referenced.Contains(this) || Databases.Any(referenced.Contains);
    }

    /// <summary>Grants each compute environment hosting a referencing app the database User role on every database (as the official -roles module).</summary>
    private void AddRoleAssignments(PulumiProgram program, PipelineStepContext ctx)
    {
        var envs = new HashSet<IPulumiManagedIdentityResource>();
        foreach (var consumer in ctx.Model.GetComputeResources().Where(IsReferencedBy))
            foreach (var env in consumer.Annotations.OfType<DeploymentTargetAnnotation>()
                         .Select(a => a.ComputeEnvironment).OfType<IPulumiManagedIdentityResource>())
                envs.Add(env);

        foreach (var env in envs)
        {
            var envId = PulumiAzureProgramExtensions.ToPulumiId(env.Name);
            foreach (var db in Databases)
            {
                program.Resources[$"{Id}{db.Id}{envId}User"] = new Dictionary<string, object?>
                {
                    ["type"] = "azure-native:kusto:DatabasePrincipalAssignment",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["resourceGroupName"] = ResourceGroupRef(program),
                        ["clusterName"] = $"${{{Id}.name}}",
                        ["databaseName"] = db.DatabaseName,
                        ["principalId"] = env.PrincipalIdExpression,
                        ["principalType"] = "App",
                        ["role"] = "User",
                    },
                    ["options"] = new Dictionary<string, object?> { ["dependsOn"] = new List<object?> { $"${{{Id}{db.Id}}}" } },
                };
            }
        }
    }

    // The cluster's resourceGroupName property, reused for the role assignments so they land next to it.
    private string ResourceGroupRef(PulumiProgram program) =>
        (string)((Dictionary<string, object?>)((Dictionary<string, object?>)program.Resources[Id]!)["properties"]!)["resourceGroupName"]!;
}

/// <summary>A read-write database of an <see cref="AzureKustoClusterResource"/> (connection string = cluster connection string + <c>;Initial Catalog=</c>).</summary>
public sealed class AzureKustoReadWriteDatabaseResource(string name, string databaseName, AzureKustoClusterResource parent)
    : Resource(name), IResourceWithConnectionString, IResourceWithParent<AzureKustoClusterResource>
{
    public AzureKustoClusterResource Parent { get; } = parent;
    public string DatabaseName { get; set; } = databaseName;
    /// <summary>KQL control commands run against the database after creation (see WithCreationScript).</summary>
    public string? CreationScript { get; set; }
    internal string Id => PulumiAzureProgramExtensions.ToPulumiId(Name);

    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"{Parent.ConnectionStringExpression};Initial Catalog={DatabaseName}");
}
