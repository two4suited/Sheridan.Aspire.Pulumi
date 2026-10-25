using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using AspirePulumiPackages.Pulumi.Azure.Base;
using AspirePulumiPackages.Pulumi.Azure.KeyVault;

namespace AspirePulumiPackages.Pulumi.Azure.Redis;

/// <summary>
/// Shared behavior of the two Redis flavors: Microsoft Entra (default) vs access-key authentication, the Key Vault that
/// holds the access-key connection string, and data-plane access for the identities of compute environments hosting
/// resources that reference the cache.
/// </summary>
public abstract class PulumiAzureRedisResourceBase(string name) : PulumiAzureResource(name), IResourceWithConnectionString
{
    /// <summary>True when access-key authentication is on (set by <c>WithAccessKeyAuthentication</c>); otherwise Entra only.</summary>
    public bool UseAccessKeyAuthentication { get; internal set; }

    /// <summary>Explicit Key Vault for the access-key secrets; when null and key auth is on, a vault is created next to the cache.</summary>
    internal PulumiAzureKeyVaultResource? KeyVault { get; set; }

    /// <summary>Name of an existing cache (set by PublishAsExisting); when set, nothing is created for the cache itself.</summary>
    public string? ExistingCacheName { get; internal set; }

    /// <summary>Local container used instead of the Pulumi-provisioned cache when running (see <c>RunAsContainer</c>).</summary>
    internal RedisResource? Container { get; set; }

    internal InvalidOperationException Unbound() => new($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack().");

    internal PulumiOutputReference OutputRef(string suffix) => Output(Stack ?? throw Unbound(), suffix);

    /// <summary>Output reference to the cache host name.</summary>
    public PulumiOutputReference HostNameOutput => OutputRef("HostName");

    /// <summary>Output reference to the cache name.</summary>
    public PulumiOutputReference NameOutputReference => OutputRef("Name");

    /// <summary>Output holding the Key Vault secret URI of the connection string (access-key authentication only).</summary>
    internal PulumiOutputReference ConnectionStringSecretUri => OutputRef("ConnectionStringSecretUri");

    public ReferenceExpression ConnectionStringExpression
    {
        get
        {
            if (Container is { } container) return container.ConnectionStringExpression;
            // Access keys never appear in stack outputs; consumers get the vault secret URI (like PulumiAzureKeyVaultResource.GetSecret).
            if (UseAccessKeyAuthentication) return ReferenceExpression.Create($"{ConnectionStringSecretUri}");
            return ReferenceExpression.Create($"{OutputRef("ConnectionString")}");
        }
    }

    /// <summary>The Pulumi reference (resource or lookup variable) exposing <c>name</c>/<c>hostName</c> of the cache.</summary>
    protected string CacheRef => ExistingCacheName is null ? Id : $"{Id}Existing";

    /// <summary>Compute environments hosting a resource that references this cache.</summary>
    protected IEnumerable<IPulumiManagedIdentityResource> ConsumerEnvironments(PipelineStepContext ctx) =>
        ctx.Model.GetComputeResources()
            .Where(r => r.Annotations.OfType<ResourceRelationshipAnnotation>().Any(a => a.Resource == this))
            .SelectMany(r => r.Annotations.OfType<DeploymentTargetAnnotation>().Select(a => a.ComputeEnvironment))
            .OfType<IPulumiManagedIdentityResource>()
            .Distinct();

    /// <summary>
    /// Stores the connection string (and any extra secrets) in a Key Vault, creating the vault when none was given, and
    /// lets referencing compute environments read it (Key Vault Secrets User), as the official integration does.
    /// The first secret must be the connection string.
    /// </summary>
    protected void AddAccessKeySecrets(PulumiProgram program, PipelineStepContext context, string resourceGroup, string location,
        IReadOnlyList<(string Id, string SecretName, string Value)> secrets)
    {
        string vault;
        var dependsOn = new List<object?>();
        if (KeyVault is { } explicitVault)
        {
            // The vault is another resource of the same stack; the deploying principal needs write access
            // (see WithDeployerRoleAssignments on the vault).
            vault = explicitVault.ExistingVaultName is null ? explicitVault.Id : $"{explicitVault.Id}Existing";
            if (explicitVault.Stack is { } vaultStack && Stack is { } stack && vaultStack != stack)
                throw new InvalidOperationException($"'{Name}' and its Key Vault '{explicitVault.Name}' must be bound to the same Pulumi stack.");
        }
        else
        {
            vault = $"{Id}Kv";
            program.AddKeyVault(vault, resourceGroup, location);
            ((Dictionary<string, object?>)((Dictionary<string, object?>)program.Resources[vault]!)["properties"]!)["tags"] =
                new Dictionary<string, object?> { ["aspire-resource-name"] = $"{Name}-kv" };
            // RBAC vault: the principal running Pulumi must be able to write the secrets.
            var deployer = program.AddRoleAssignment($"{vault}Deployer", PulumiAzureProgramExtensions.CurrentPrincipalObjectId,
                AzureRoleDefinitions.KeyVaultSecretsOfficer, $"${{{vault}.id}}", "User");
            dependsOn.Add($"${{{deployer}}}");
            program.Outputs[$"{Id}KvName"] = $"${{{vault}.name}}";
            program.Outputs[$"{Id}KvVaultUri"] = $"${{{vault}.properties.vaultUri}}";
        }

        foreach (var (id, secretName, value) in secrets)
        {
            var secretId = $"{Id}{id}";
            program.AddKeyVaultSecret(secretId, vault, resourceGroup, secretName, value);
            if (dependsOn.Count > 0)
                ((Dictionary<string, object?>)program.Resources[secretId]!)["options"] = new Dictionary<string, object?> { ["dependsOn"] = dependsOn };
            program.Outputs[$"{secretId}Uri"] = $"${{{secretId}.properties.secretUri}}";
        }
        program.Outputs[$"{Id}ConnectionStringSecretUri"] = $"${{{Id}{secrets[0].Id}.properties.secretUri}}";

        foreach (var env in ConsumerEnvironments(context))
            program.AddRoleAssignment($"{Id}{PulumiAzureProgramExtensions.ToPulumiId(env.Name)}KvSecretsUser", env.PrincipalIdExpression,
                AzureRoleDefinitions.KeyVaultSecretsUser, $"${{{vault}.id}}");
    }

    internal static Dictionary<string, object?> Invoke(string function, Dictionary<string, object?> arguments) => new()
    {
        ["fn::invoke"] = new Dictionary<string, object?> { ["function"] = function, ["arguments"] = arguments },
    };
}

/// <summary>
/// Azure Cache for Redis (<c>Microsoft.Cache/redis</c>) provisioned by Pulumi (azure-native), mirroring the Bicep of the official
/// <c>AzureRedisCacheResource</c>: Basic C1, TLS 1.2, non-SSL port off. Entra authentication (access keys disabled) by default,
/// in which case compute environments hosting referencing apps get the <c>Data Contributor</c> access policy.
/// </summary>
public sealed class PulumiAzureRedisCacheResource(string name) : PulumiAzureRedisResourceBase(name)
{
    public const string DataContributorPolicy = "Data Contributor";

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        if (ExistingCacheName is { } existing)
        {
            program.Variables[CacheRef] = Invoke("azure-native:redis:getRedis",
                new() { ["name"] = existing, ["resourceGroupName"] = rg });
        }
        else
        {
            var props = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = rg,
                ["location"] = location,
                ["sku"] = new Dictionary<string, object?> { ["name"] = "Basic", ["family"] = "C", ["capacity"] = 1 },
                ["enableNonSslPort"] = false,
                ["minimumTlsVersion"] = "1.2",
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
            };
            if (!UseAccessKeyAuthentication)
            {
                props["disableAccessKeyAuthentication"] = true;
                props["redisConfiguration"] = new Dictionary<string, object?> { ["aadEnabled"] = "true" };
            }
            // No name: the provider auto-names it (<id>-<suffix>), the equivalent of take('<name>-${uniqueString(...)}', 63).
            program.Resources[Id] = new Dictionary<string, object?> { ["type"] = "azure-native:redis:Redis", ["properties"] = props };
        }

        program.Outputs[$"{Id}HostName"] = $"${{{CacheRef}.hostName}}";
        program.Outputs[$"{Id}Name"] = $"${{{CacheRef}.name}}";
        program.Outputs[$"{Id}Id"] = $"${{{CacheRef}.id}}";

        if (UseAccessKeyAuthentication)
        {
            var keys = $"{Id}Keys";
            program.Variables[keys] = Invoke("azure-native:redis:listRedisKeys",
                new() { ["name"] = $"${{{CacheRef}.name}}", ["resourceGroupName"] = rg });
            AddAccessKeySecrets(program, context, rg, location,
            [
                ("ConnectionStringSecret", $"connectionstrings--{Name}", $"${{{CacheRef}.hostName}},ssl=true,password=${{{keys}.primaryKey}}"),
            ]);
        }
        else
        {
            program.Outputs[$"{Id}ConnectionString"] = $"${{{CacheRef}.hostName}},ssl=true";
            foreach (var env in ConsumerEnvironments(context))
            {
                var envId = PulumiAzureProgramExtensions.ToPulumiId(env.Name);
                program.Resources[$"{Id}{envId}Contributor"] = new Dictionary<string, object?>
                {
                    ["type"] = "azure-native:redis:AccessPolicyAssignment",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["resourceGroupName"] = rg,
                        ["cacheName"] = $"${{{CacheRef}.name}}",
                        ["accessPolicyAssignmentName"] = $"{Id}{envId}",
                        ["accessPolicyName"] = DataContributorPolicy,
                        ["objectId"] = env.PrincipalIdExpression,
                        ["objectIdAlias"] = env.Name,
                    },
                };
            }
        }

        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Azure Managed Redis (<c>Microsoft.Cache/redisEnterprise</c>) provisioned by Pulumi (azure-native), mirroring the Bicep of the
/// official <c>AzureManagedRedisResource</c>: a Balanced_B0 cluster with a <c>default</c> database on port 10000, TLS 1.2.
/// Entra authentication by default; compute environments hosting referencing apps get the database's <c>default</c> access policy.
/// </summary>
public sealed class PulumiAzureManagedRedisResource(string name) : PulumiAzureRedisResourceBase(name)
{
    public const int DatabasePort = 10000;
    public const string DatabaseName = "default";

    /// <summary>The cluster SKU (default Balanced_B0, as in the official integration).</summary>
    public string Sku { get; set; } = "Balanced_B0";

    /// <summary>The database port (10000).</summary>
    public int Port => DatabasePort;

    private string DatabaseRef => ExistingCacheName is null ? $"{Id}Default" : $"{Id}DefaultExisting";

    private string DatabaseNameExpression => ExistingCacheName is null ? $"${{{DatabaseRef}.name}}" : DatabaseName;

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        if (ExistingCacheName is { } existing)
        {
            program.Variables[CacheRef] = Invoke("azure-native:redisenterprise:getRedisEnterprise",
                new() { ["clusterName"] = existing, ["resourceGroupName"] = rg });
            program.Variables[DatabaseRef] = Invoke("azure-native:redisenterprise:getDatabase",
                new() { ["clusterName"] = existing, ["databaseName"] = DatabaseName, ["resourceGroupName"] = rg });
        }
        else
        {
            program.Resources[Id] = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:redisenterprise:RedisEnterprise",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["location"] = location,
                    ["sku"] = new Dictionary<string, object?> { ["name"] = Sku },
                    ["minimumTlsVersion"] = "1.2",
                    ["publicNetworkAccess"] = "Enabled",
                },
            };
            program.Resources[DatabaseRef] = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:redisenterprise:Database",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["clusterName"] = $"${{{Id}.name}}",
                    ["databaseName"] = DatabaseName,
                    ["accessKeysAuthentication"] = UseAccessKeyAuthentication ? "Enabled" : "Disabled",
                    ["port"] = DatabasePort,
                },
            };
        }

        program.Outputs[$"{Id}HostName"] = $"${{{CacheRef}.hostName}}";
        program.Outputs[$"{Id}Name"] = $"${{{CacheRef}.name}}";
        program.Outputs[$"{Id}Id"] = $"${{{CacheRef}.id}}";

        if (UseAccessKeyAuthentication)
        {
            var keys = $"{Id}Keys";
            program.Variables[keys] = Invoke("azure-native:redisenterprise:listDatabaseKeys", new()
            {
                ["clusterName"] = $"${{{CacheRef}.name}}",
                ["databaseName"] = DatabaseNameExpression,
                ["resourceGroupName"] = rg,
            });
            AddAccessKeySecrets(program, context, rg, location,
            [
                ("ConnectionStringSecret", $"connectionstrings--{Name}", $"${{{CacheRef}.hostName}}:{DatabasePort},ssl=true,password=${{{keys}.primaryKey}}"),
                ("PrimaryAccessKeySecret", $"primaryaccesskey--{Name}", $"${{{keys}.primaryKey}}"),
            ]);
        }
        else
        {
            program.Outputs[$"{Id}ConnectionString"] = $"${{{CacheRef}.hostName}}:{DatabasePort},ssl=true";
            foreach (var env in ConsumerEnvironments(context))
            {
                var envId = PulumiAzureProgramExtensions.ToPulumiId(env.Name);
                program.Resources[$"{Id}{envId}Contributor"] = new Dictionary<string, object?>
                {
                    ["type"] = "azure-native:redisenterprise:AccessPolicyAssignment",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["resourceGroupName"] = rg,
                        ["clusterName"] = $"${{{CacheRef}.name}}",
                        ["databaseName"] = DatabaseNameExpression,
                        ["accessPolicyAssignmentName"] = $"{Id}{envId}",
                        ["accessPolicyName"] = DatabaseName,
                        ["user"] = new Dictionary<string, object?> { ["objectId"] = env.PrincipalIdExpression },
                    },
                };
            }
        }

        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }
}
