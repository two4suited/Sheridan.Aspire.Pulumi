using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using AspirePulumiPackages.Pulumi;

namespace AspirePulumiPackages.Pulumi.Azure;

/// <summary>
/// An Azure Cosmos DB (NoSQL API) account provisioned by Pulumi (azure-native): serverless account with Entra-only
/// auth, SQL databases and containers, and a role assignment granting data-plane access to any compute environment
/// whose apps reference it. Use <see cref="AzureCosmosDBExtensions.RunAsEmulator"/> to run the NoSQL API emulator
/// locally instead.
/// </summary>
public sealed class AzureCosmosDBAccountResource(string name) : Resource(name), IResourceWithConnectionString, IPulumiProgramContributor
{
    private const string DataContributorRoleId = "00000000-0000-0000-0000-000000000002";

    public PulumiStackResource? Stack { get; set; }

    /// <summary>Azure region; when null, the stack's 'location' config value (default 'eastus') is used.</summary>
    public string? Location { get; set; }

    public List<AzureCosmosDBDatabaseResource> Databases { get; } = [];

    public AzureCosmosDBEmulatorResource? Emulator { get; set; }

    internal string Id => string.Concat(
        Name.Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select((p, i) => i == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));

    private PulumiOutputReference Output(string suffix) =>
        new(Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet."), $"{Id}{suffix}");

    public ReferenceExpression ConnectionStringExpression => Emulator is { } emulator
        ? ReferenceExpression.Create($"AccountEndpoint={emulator.GetEndpoint("emulator")};AccountKey={AzureCosmosDBEmulatorResource.WellKnownKey};")
        : ReferenceExpression.Create($"{Output("ConnectionString")}");

    public Task ContributeAsync(PulumiProgram program, PipelineStepContext ctx)
    {
        var location = Location ?? "${location}";
        var rg = $"${{{Id}Rg.name}}";
        program.Configuration.TryAdd("location", new Dictionary<string, object?> { ["type"] = "string", ["default"] = "eastus" });

        program.AddInfraResource($"{Id}Rg", "azure-native:resources:ResourceGroup", new()
        {
            ["type"] = "azure-native:resources:ResourceGroup",
            ["properties"] = new Dictionary<string, object?> { ["location"] = location },
        });

        program.Resources[Id] = new Dictionary<string, object?>
        {
            ["type"] = "azure-native:cosmosdb:DatabaseAccount",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = rg,
                ["location"] = location,
                ["kind"] = "GlobalDocumentDB",
                ["databaseAccountOfferType"] = "Standard",
                ["locations"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["locationName"] = location, ["failoverPriority"] = 0 },
                },
                ["capabilities"] = new List<object?> { new Dictionary<string, object?> { ["name"] = "EnableServerless" } },
                ["consistencyPolicy"] = new Dictionary<string, object?> { ["defaultConsistencyLevel"] = "Session" },
                ["disableLocalAuth"] = true,
            },
        };

        foreach (var db in Databases)
        {
            var dbId = $"{Id}{db.Id}";
            program.Resources[dbId] = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:cosmosdb:SqlResourceSqlDatabase",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["accountName"] = $"${{{Id}.name}}",
                    ["location"] = location,
                    ["resource"] = new Dictionary<string, object?> { ["id"] = db.Name },
                },
            };

            foreach (var container in db.Containers)
            {
                program.Resources[$"{dbId}{container.Id}"] = new Dictionary<string, object?>
                {
                    ["type"] = "azure-native:cosmosdb:SqlResourceSqlContainer",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["resourceGroupName"] = rg,
                        ["accountName"] = $"${{{Id}.name}}",
                        ["databaseName"] = $"${{{dbId}.name}}",
                        ["location"] = location,
                        ["resource"] = new Dictionary<string, object?>
                        {
                            ["id"] = container.Name,
                            ["partitionKey"] = new Dictionary<string, object?>
                            {
                                ["paths"] = container.PartitionKeyPaths.Cast<object?>().ToList(),
                                ["kind"] = container.PartitionKeyPaths.Count > 1 ? "MultiHash" : "Hash",
                            },
                        },
                    },
                };
            }
        }

        program.Outputs[$"{Id}ConnectionString"] = $"${{{Id}.documentEndpoint}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";

        AddRoleAssignments(program, ctx, rg);

        return Task.CompletedTask;
    }

    /// <summary>Grants the shared identity of every compute environment hosting a referencing app the built-in Cosmos DB Data Contributor role.</summary>
    private void AddRoleAssignments(PulumiProgram program, PipelineStepContext ctx, string rg)
    {
        var environments = ctx.Model.GetComputeResources()
            .Where(r => r.Annotations.OfType<ResourceRelationshipAnnotation>().Any(a => a.Resource == this))
            .SelectMany(r => r.Annotations.OfType<DeploymentTargetAnnotation>())
            .Select(a => a.ComputeEnvironment)
            .OfType<IPulumiManagedIdentityResource>()
            .Distinct()
            .ToList();

        if (environments.Count == 0) return;

        // The built-in role definition's id is deterministic (no live lookup needed): every Cosmos DB account
        // exposes it as a child resource at this fixed path the moment the account exists.
        var dataContributorRoleId = $"${{{Id}.id}}/sqlRoleDefinitions/{DataContributorRoleId}";

        foreach (var env in environments)
        {
            var envId = string.Concat(
                env.Name.Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries)
                    .Select((p, i) => i == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));

            program.Resources[$"{Id}{envId}RoleAssignment"] = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:cosmosdb:SqlResourceSqlRoleAssignment",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["accountName"] = $"${{{Id}.name}}",
                    ["principalId"] = env.PrincipalIdExpression,
                    ["roleDefinitionId"] = dataContributorRoleId,
                },
            };
        }
    }
}

/// <summary>A SQL (NoSQL API) database within an <see cref="AzureCosmosDBAccountResource"/>.</summary>
public sealed class AzureCosmosDBDatabaseResource(string name, AzureCosmosDBAccountResource parent) : Resource(name)
{
    public AzureCosmosDBAccountResource Parent { get; } = parent;
    public List<AzureCosmosDBContainerResource> Containers { get; } = [];

    internal string Id => string.Concat(
        Name.Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select((p, i) => i == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));
}

/// <summary>A container within an <see cref="AzureCosmosDBDatabaseResource"/>.</summary>
public sealed class AzureCosmosDBContainerResource(string name, IReadOnlyList<string> partitionKeyPaths, AzureCosmosDBDatabaseResource parent) : Resource(name)
{
    public AzureCosmosDBDatabaseResource Parent { get; } = parent;
    public IReadOnlyList<string> PartitionKeyPaths { get; } = partitionKeyPaths;

    internal string Id => string.Concat(
        Name.Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select((p, i) => i == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));
}

/// <summary>The Cosmos DB NoSQL API emulator container, run locally in place of the real Pulumi-provisioned account.</summary>
public sealed class AzureCosmosDBEmulatorResource(AzureCosmosDBAccountResource innerResource) : ContainerResource(innerResource.Name + "-emulator")
{
    /// <summary>The fixed, publicly documented master key every Cosmos DB emulator accepts.</summary>
    public const string WellKnownKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    public AzureCosmosDBAccountResource InnerResource { get; } = innerResource;
}
