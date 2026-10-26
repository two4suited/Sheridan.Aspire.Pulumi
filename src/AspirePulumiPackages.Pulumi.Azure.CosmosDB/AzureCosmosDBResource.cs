using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using AspirePulumiPackages.Pulumi.Azure.Base;

namespace AspirePulumiPackages.Pulumi.Azure.CosmosDB;

/// <summary>
/// An Azure Cosmos DB (NoSQL API) account provisioned by Pulumi (azure-native), mirroring Aspire.Hosting.Azure.CosmosDB's
/// Bicep: a serverless GlobalDocumentDB account (Session consistency) with Entra-only auth by default
/// (<c>disableLocalAuth: true</c>), SQL databases and containers, and the built-in Cosmos DB Data Contributor role
/// granted to compute environments hosting resources that reference it. With
/// <see cref="AzureCosmosDBExtensions.WithAccessKeyAuthentication"/> local auth stays enabled and the connection strings
/// (account, database, container) are written as Key Vault secrets instead.
/// </summary>
public sealed class AzureCosmosDBResource(string name) : PulumiAzureResource(name), IResourceWithConnectionString
{
    /// <summary>Built-in "Cosmos DB Built-in Data Contributor" SQL role definition (a fixed child of every account).</summary>
    public const string DataContributorRoleId = "00000000-0000-0000-0000-000000000002";

    public List<AzureCosmosDBDatabaseResource> Databases { get; } = [];

    public AzureCosmosDBEmulatorResource? Emulator { get; set; }

    /// <summary>True when the account authenticates with access keys (secrets in a Key Vault) rather than Entra identities.</summary>
    public bool UseAccessKeyAuthentication { get; set; }

    /// <summary>When false, the account is not serverless (the Azure default SKU; see WithDefaultAzureSku).</summary>
    public bool Serverless { get; set; } = true;

    internal PulumiOutputReference OutputRef(string suffix) =>
        Output(Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack()."), suffix);

    /// <summary>Output reference to the account's document endpoint.</summary>
    public PulumiOutputReference ConnectionString => OutputRef("ConnectionString");

    public PulumiOutputReference NameOutput => OutputRef("Name");

    /// <summary>Output reference to a Key Vault secret URI (access key mode only).</summary>
    internal PulumiOutputReference SecretUri(string secretId) => OutputRef($"{secretId}Uri");

    internal const string ConnectionStringSecretId = "KvSecretConnectionString";
    internal string KeyVaultId => $"{Id}Kv";

    /// <summary>
    /// In emulator mode, the emulator connection string; in access key mode, the URI of the Key Vault secret holding the
    /// connection string (resolve it from Key Vault in the app); otherwise the account's document endpoint.
    /// </summary>
    public ReferenceExpression ConnectionStringExpression => Emulator is { } e
        ? EmulatorConnectionString(e)
        : UseAccessKeyAuthentication
            ? ReferenceExpression.Create($"{SecretUri(ConnectionStringSecretId)}")
            : ReferenceExpression.Create($"{ConnectionString}");

    private static ReferenceExpression EmulatorConnectionString(AzureCosmosDBEmulatorResource e) =>
        ReferenceExpression.Create($"AccountEndpoint={e.GetEndpoint("emulator")};AccountKey={AzureCosmosDBEmulatorResource.WellKnownKey};DisableServerCertificateValidation=True;");

    /// <summary>The connection string for a database (and optionally a container) of this account.</summary>
    internal ReferenceExpression ChildConnectionString(string databaseName, string? containerName, string secretId)
    {
        if (Emulator is { } e)
            return containerName is null
                ? ReferenceExpression.Create($"{EmulatorConnectionString(e)}Database={databaseName};")
                : ReferenceExpression.Create($"{EmulatorConnectionString(e)}Database={databaseName};Container={containerName};");
        if (UseAccessKeyAuthentication) return ReferenceExpression.Create($"{SecretUri(secretId)}");
        return containerName is null
            ? ReferenceExpression.Create($"AccountEndpoint={ConnectionString};Database={databaseName};")
            : ReferenceExpression.Create($"AccountEndpoint={ConnectionString};Database={databaseName};Container={containerName};");
    }

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        // No accountName: the azure-native provider auto-names it (<id> + random suffix, lowercase), the Pulumi
        // equivalent of Bicep's take('<name>-${uniqueString(resourceGroup().id)}', 44).
        var props = new Dictionary<string, object?>
        {
            ["resourceGroupName"] = rg,
            ["location"] = location,
            ["kind"] = "GlobalDocumentDB",
            ["databaseAccountOfferType"] = "Standard",
            ["locations"] = new List<object?>
            {
                new Dictionary<string, object?> { ["locationName"] = location, ["failoverPriority"] = 0 },
            },
            ["consistencyPolicy"] = new Dictionary<string, object?> { ["defaultConsistencyLevel"] = "Session" },
            ["disableLocalAuth"] = !UseAccessKeyAuthentication,
            ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
        };
        if (Serverless)
            props["capabilities"] = new List<object?> { new Dictionary<string, object?> { ["name"] = "EnableServerless" } };
        program.Resources[Id] = new Dictionary<string, object?> { ["type"] = "azure-native:cosmosdb:DatabaseAccount", ["properties"] = props };

        foreach (var db in Databases)
        {
            var dbId = $"{Id}{db.Id}";
            // databaseName/containerName are set explicitly (as in Bicep) so the resource name is the real Cosmos name.
            program.Resources[dbId] = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:cosmosdb:SqlResourceSqlDatabase",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["accountName"] = $"${{{Id}.name}}",
                    ["databaseName"] = db.DatabaseName,
                    ["location"] = location,
                    ["resource"] = new Dictionary<string, object?> { ["id"] = db.DatabaseName },
                },
            };

            foreach (var container in db.Containers)
            {
                var partitionKey = new Dictionary<string, object?>
                {
                    ["paths"] = container.PartitionKeyPaths.Cast<object?>().ToList(),
                    ["kind"] = container.PartitionKeyPaths.Count > 1 ? "MultiHash" : "Hash",
                };
                if (container.PartitionKeyPaths.Count > 1) partitionKey["version"] = 2;

                program.Resources[$"{dbId}{container.Id}"] = new Dictionary<string, object?>
                {
                    ["type"] = "azure-native:cosmosdb:SqlResourceSqlContainer",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["resourceGroupName"] = rg,
                        ["accountName"] = $"${{{Id}.name}}",
                        ["databaseName"] = $"${{{dbId}.name}}",
                        ["containerName"] = container.ContainerName,
                        ["location"] = location,
                        ["resource"] = new Dictionary<string, object?> { ["id"] = container.ContainerName, ["partitionKey"] = partitionKey },
                    },
                };
            }
        }

        program.Outputs[$"{Id}ConnectionString"] = $"${{{Id}.documentEndpoint}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";

        if (UseAccessKeyAuthentication) AddKeyVaultSecrets(program, rg, location);
        AddRoleAssignments(program, context, rg);
        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Access key mode: a dedicated RBAC Key Vault holding the connection strings and primary key as secrets
    /// (<c>connectionstrings--name</c>, <c>primaryaccesskey--name</c>), as the official integration does.
    /// </summary>
    private void AddKeyVaultSecrets(PulumiProgram program, string rg, string location)
    {
        var kv = KeyVaultId;
        program.AddKeyVault(kv, rg, location);
        ((Dictionary<string, object?>)((Dictionary<string, object?>)program.Resources[kv]!)["properties"]!)["tags"] =
            new Dictionary<string, object?> { ["aspire-resource-name"] = $"{Name}-kv" };

        var keys = $"{Id}Keys";
        program.Variables[keys] = new Dictionary<string, object?>
        {
            ["fn::invoke"] = new Dictionary<string, object?>
            {
                ["function"] = "azure-native:cosmosdb:listDatabaseAccountKeys",
                ["arguments"] = new Dictionary<string, object?> { ["accountName"] = $"${{{Id}.name}}", ["resourceGroupName"] = rg },
            },
        };

        // The deploying principal must be able to write secrets to the RBAC vault.
        var officer = program.AddRoleAssignment($"{kv}DeployerSecretsOfficer", PulumiAzureProgramExtensions.CurrentPrincipalObjectId,
            AzureRoleDefinitions.KeyVaultSecretsOfficer, $"${{{kv}.id}}", "User");

        var endpoint = $"${{{Id}.documentEndpoint}}";
        var key = $"${{{keys}.primaryMasterKey}}";
        var account = $"AccountEndpoint={endpoint};AccountKey={key}";

        void Secret(string id, string secretName, string value)
        {
            program.AddKeyVaultSecret($"{Id}{id}", kv, rg, secretName, value);
            ((Dictionary<string, object?>)program.Resources[$"{Id}{id}"]!)["options"] =
                new Dictionary<string, object?> { ["dependsOn"] = new List<object?> { $"${{{officer}}}" } };
            program.Outputs[$"{Id}{id}Uri"] = $"${{{Id}{id}.properties.secretUri}}";
        }

        Secret(ConnectionStringSecretId, $"connectionstrings--{Name}", account);
        Secret("KvSecretPrimaryAccessKey", $"primaryaccesskey--{Name}", key);
        foreach (var db in Databases)
        {
            Secret(db.SecretId, $"connectionstrings--{db.Name}", $"{account};Database={db.DatabaseName}");
            foreach (var c in db.Containers)
                Secret(c.SecretId, $"connectionstrings--{c.Name}", $"{account};Database={db.DatabaseName};Container={c.ContainerName}");
        }

        program.Outputs[$"{kv}VaultUri"] = $"${{{kv}.properties.vaultUri}}";
        program.Outputs[$"{kv}Name"] = $"${{{kv}.name}}";
    }

    /// <summary>
    /// Identity mode: grants the shared identity of every compute environment hosting a referencing app the built-in
    /// Cosmos DB Data Contributor role on the account. Access key mode: Key Vault Secrets User on the secrets vault.
    /// </summary>
    private void AddRoleAssignments(PulumiProgram program, PipelineStepContext ctx, string rg)
    {
        var related = new List<IResource> { this };
        related.AddRange(Databases);
        related.AddRange(Databases.SelectMany(d => d.Containers));

        var environments = ctx.Model.GetComputeResources()
            .Where(r => r.Annotations.OfType<ResourceRelationshipAnnotation>().Any(a => related.Contains(a.Resource)))
            .SelectMany(r => r.Annotations.OfType<DeploymentTargetAnnotation>())
            .Select(a => a.ComputeEnvironment)
            .OfType<IPulumiManagedIdentityResource>()
            .Distinct()
            .ToList();

        foreach (var env in environments)
        {
            var envId = PulumiAzureProgramExtensions.ToPulumiId(env.Name);
            if (UseAccessKeyAuthentication)
            {
                program.AddRoleAssignment($"{KeyVaultId}{envId}SecretsUser", env.PrincipalIdExpression,
                    AzureRoleDefinitions.KeyVaultSecretsUser, $"${{{KeyVaultId}.id}}");
                continue;
            }

            // The built-in role definition's id is deterministic (no live lookup needed): every Cosmos DB account
            // exposes it as a child resource at this fixed path the moment the account exists.
            program.Resources[$"{Id}{envId}RoleAssignment"] = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:cosmosdb:SqlResourceSqlRoleAssignment",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["accountName"] = $"${{{Id}.name}}",
                    // Cosmos requires the assignment's name to be a GUID; derive a stable one from the logical name.
                    ["roleAssignmentId"] = new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"{Id}{envId}RoleAssignment"))).ToString(),
                    ["principalId"] = env.PrincipalIdExpression,
                    ["roleDefinitionId"] = $"${{{Id}.id}}/sqlRoleDefinitions/{DataContributorRoleId}",
                    ["scope"] = $"${{{Id}.id}}",
                },
            };
        }
    }
}

/// <summary>A SQL (NoSQL API) database within an <see cref="AzureCosmosDBResource"/>.</summary>
public sealed class AzureCosmosDBDatabaseResource(string name, string databaseName, AzureCosmosDBResource parent)
    : Resource(name), IResourceWithConnectionString, IResourceWithParent<AzureCosmosDBResource>
{
    public AzureCosmosDBResource Parent { get; } = parent;
    public string DatabaseName { get; } = databaseName;
    public List<AzureCosmosDBContainerResource> Containers { get; } = [];

    internal string Id => PulumiAzureProgramExtensions.ToPulumiId(Name);
    internal string SecretId => $"KvSecret{Id}";

    public ReferenceExpression ConnectionStringExpression => Parent.ChildConnectionString(DatabaseName, null, SecretId);
}

/// <summary>A container within an <see cref="AzureCosmosDBDatabaseResource"/>.</summary>
public sealed class AzureCosmosDBContainerResource(string name, string containerName, IReadOnlyList<string> partitionKeyPaths, AzureCosmosDBDatabaseResource parent)
    : Resource(name), IResourceWithConnectionString, IResourceWithParent<AzureCosmosDBDatabaseResource>
{
    public AzureCosmosDBDatabaseResource Parent { get; } = parent;
    public string ContainerName { get; } = containerName;
    public IReadOnlyList<string> PartitionKeyPaths { get; } = partitionKeyPaths;

    internal string Id => PulumiAzureProgramExtensions.ToPulumiId(Name);
    internal string SecretId => $"KvSecret{Parent.Id}{Id}";

    public ReferenceExpression ConnectionStringExpression => Parent.Parent.ChildConnectionString(Parent.DatabaseName, ContainerName, SecretId);
}

/// <summary>The Cosmos DB NoSQL API emulator container, run locally in place of the real Pulumi-provisioned account.</summary>
public sealed class AzureCosmosDBEmulatorResource(AzureCosmosDBResource innerResource) : ContainerResource(innerResource.Name + "-emulator")
{
    /// <summary>The fixed, publicly documented master key every Cosmos DB emulator accepts.</summary>
    public const string WellKnownKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    public AzureCosmosDBResource InnerResource { get; } = innerResource;

    /// <summary>True for the classic (Windows-derived) emulator image, false for the Linux vNext one.</summary>
    public bool IsClassic { get; internal set; }
}
