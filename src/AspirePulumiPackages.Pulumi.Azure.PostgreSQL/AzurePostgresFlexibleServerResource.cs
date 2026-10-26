using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using AspirePulumiPackages.Pulumi.Azure.Base;

namespace AspirePulumiPackages.Pulumi.Azure.PostgreSQL;

/// <summary>
/// An Azure Database for PostgreSQL flexible server provisioned by Pulumi (azure-native), mirroring
/// Aspire.Hosting.Azure.PostgreSQL's Bicep: version 16, Standard_B1ms (Burstable), 32 GB, 7 day backups, no HA, the
/// AllowAllAzureIps firewall rule and the databases added to the builder. By default only Microsoft Entra
/// authentication is enabled and the identity of each compute environment hosting a referencing app is made a server
/// administrator; with <c>WithPasswordAuthentication</c> password authentication is used instead and the connection
/// strings are also stored as secrets in a Key Vault the app identities can read.
/// </summary>
public sealed class AzurePostgresFlexibleServerResource(string name) : PulumiAzureResource(name), IResourceWithConnectionString
{
    public const string KeyVaultSecretsUserRoleId = AzureRoleDefinitions.KeyVaultSecretsUser;
    public const string KeyVaultSecretsOfficerRoleId = AzureRoleDefinitions.KeyVaultSecretsOfficer;

    internal List<AzurePostgresFlexibleServerDatabaseResource> Databases { get; } = [];

    /// <summary>True when password authentication (instead of Microsoft Entra) is used.</summary>
    public bool UsePasswordAuthentication { get; internal set; }

    /// <summary>Administrator login parameter (password authentication); when null a Pulumi config entry is declared.</summary>
    public ParameterResource? UserNameParameter { get; internal set; }

    /// <summary>Administrator password parameter (password authentication); when null a secret Pulumi config entry is declared.</summary>
    public ParameterResource? PasswordParameter { get; internal set; }

    /// <summary>The local postgres container that replaces this server in run mode (see <c>RunAsContainer</c>).</summary>
    public PostgresServerResource? InnerResource { get; internal set; }

    /// <summary>Whether the server runs as a local container instead of in Azure.</summary>
    public bool IsContainer => InnerResource is not null;

    private PulumiStackResource BoundStack => Stack ?? throw new InvalidOperationException(
        $"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack().");

    internal string UserNameConfigKey => UserNameParameter is { } p ? PulumiAzureProgramExtensions.ToPulumiId(p.Name) : $"{Id}AdministratorLogin";
    internal string PasswordConfigKey => PasswordParameter is { } p ? PulumiAzureProgramExtensions.ToPulumiId(p.Name) : $"{Id}AdministratorPassword";

    /// <summary>The server's fully qualified domain name.</summary>
    public PulumiOutputReference HostName => Output(BoundStack, "HostName");

    public PulumiOutputReference NameOutputReference => Output(BoundStack, "Name");

    /// <summary>Key Vault URI holding the connection-string secrets (password authentication only).</summary>
    public PulumiOutputReference KeyVaultUri => Output(BoundStack, "KeyVaultUri");

    /// <summary>Full connection string, including credentials (secret output; password authentication only).</summary>
    internal PulumiOutputReference ConnectionStringOutput => Output(BoundStack, "ConnectionString");

    /// <summary>Connection string: <c>Host=...</c> for Entra authentication, with <c>Username</c>/<c>Password</c> for password authentication.</summary>
    public ReferenceExpression ConnectionStringExpression =>
        InnerResource is { } inner
            ? inner.ConnectionStringExpression
            : UsePasswordAuthentication
                ? ReferenceExpression.Create($"{ConnectionStringOutput}")
                : ReferenceExpression.Create($"Host={HostName}");

    internal string DatabaseSecretId(AzurePostgresFlexibleServerDatabaseResource db) => $"{Id}{db.Id}ConnectionStringSecret";

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        var dependsOnChain = new List<object?>();

        var properties = new Dictionary<string, object?>
        {
            ["resourceGroupName"] = rg,
            ["location"] = location,
            ["version"] = "16",
            ["sku"] = new Dictionary<string, object?> { ["name"] = "Standard_B1ms", ["tier"] = "Burstable" },
            ["storage"] = new Dictionary<string, object?> { ["storageSizeGB"] = 32 },
            ["backup"] = new Dictionary<string, object?> { ["backupRetentionDays"] = 7, ["geoRedundantBackup"] = "Disabled" },
            ["highAvailability"] = new Dictionary<string, object?> { ["mode"] = "Disabled" },
            ["availabilityZone"] = "1",
            ["authConfig"] = new Dictionary<string, object?>
            {
                ["activeDirectoryAuth"] = UsePasswordAuthentication ? "Disabled" : "Enabled",
                ["passwordAuth"] = UsePasswordAuthentication ? "Enabled" : "Disabled",
            },
            ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
        };

        if (UsePasswordAuthentication)
        {
            Provisioning.PulumiProgramExtensions.AddParameter(program, UserNameConfigKey,
                defaultValue: UserNameParameter is null ? "pgadmin" : null, secret: UserNameParameter?.Secret ?? false);
            Provisioning.PulumiProgramExtensions.AddParameter(program, PasswordConfigKey, secret: true);
            properties["administratorLogin"] = $"${{{UserNameConfigKey}}}";
            properties["administratorLoginPassword"] = $"${{{PasswordConfigKey}}}";
        }

        // No serverName: the azure-native provider auto-names the server (<id> + random suffix), the Pulumi equivalent of
        // Bicep's take('<name>${uniqueString(resourceGroup().id)}', 63).
        program.Resources[Id] = new Dictionary<string, object?> { ["type"] = "azure-native:dbforpostgresql:Server", ["properties"] = properties };

        Dictionary<string, object?> Child(string type, Dictionary<string, object?> extra)
        {
            var props = new Dictionary<string, object?> { ["resourceGroupName"] = rg, ["serverName"] = $"${{{Id}.name}}" };
            foreach (var (k, v) in extra) props[k] = v;
            return new() { ["type"] = type, ["properties"] = props };
        }

        // Server operations are serialized by Azure (a busy server rejects concurrent changes), so the children are chained.
        string Chain(string id, Dictionary<string, object?> def)
        {
            if (dependsOnChain.Count > 0)
                def["options"] = new Dictionary<string, object?> { ["dependsOn"] = new List<object?>(dependsOnChain) };
            program.Resources[id] = def;
            dependsOnChain.Clear();
            dependsOnChain.Add($"${{{id}}}");
            return id;
        }

        Chain($"{Id}FirewallRuleAllowAllAzureIps", Child("azure-native:dbforpostgresql:FirewallRule", new()
        {
            ["firewallRuleName"] = "AllowAllAzureIps",
            ["startIpAddress"] = "0.0.0.0",
            ["endIpAddress"] = "0.0.0.0",
        }));

        foreach (var db in Databases)
            Chain($"{Id}{db.Id}", Child("azure-native:dbforpostgresql:Database", new() { ["databaseName"] = db.DatabaseName }));

        program.Outputs[$"{Id}HostName"] = $"${{{Id}.fullyQualifiedDomainName}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";

        var environments = ReferencingEnvironments(context);

        if (UsePasswordAuthentication)
            AddKeyVaultAndSecrets(program, rg, location, environments);
        else
            AddEntraAdministrators(program, environments, dependsOnChain);

        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }

    /// <summary>Compute environments (with a managed identity) hosting an app that references the server or one of its databases.</summary>
    private List<IPulumiManagedIdentityResource> ReferencingEnvironments(PipelineStepContext ctx)
    {
        var environments = new List<IPulumiManagedIdentityResource>();
        foreach (var app in ctx.Model.GetComputeResources())
        {
            var referenced = app.Annotations.OfType<ResourceRelationshipAnnotation>().Select(a => a.Resource).ToList();
            if (!referenced.Contains(this) && !Databases.Any(referenced.Contains)) continue;
            foreach (var env in app.Annotations.OfType<DeploymentTargetAnnotation>()
                         .Select(a => a.ComputeEnvironment).OfType<IPulumiManagedIdentityResource>())
                if (!environments.Contains(env)) environments.Add(env);
        }
        return environments;
    }

    /// <summary>Entra authentication: each environment's shared identity becomes a server administrator (as the official <c>-roles-</c> module does).</summary>
    private void AddEntraAdministrators(PulumiProgram program, List<IPulumiManagedIdentityResource> environments, List<object?> dependsOn)
    {
        foreach (var env in environments)
        {
            var envId = PulumiAzureProgramExtensions.ToPulumiId(env.Name);
            // The identity's display name is the sibling of its principalId on the same Pulumi resource.
            var principalName = env.PrincipalIdExpression.Replace(".principalId}", ".name}", StringComparison.Ordinal);
            var id = $"{Id}{envId}Administrator";
            program.Resources[id] = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:dbforpostgresql:Administrator",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = ((Dictionary<string, object?>)((Dictionary<string, object?>)program.Resources[Id]!)["properties"]!)["resourceGroupName"],
                    ["serverName"] = $"${{{Id}.name}}",
                    ["objectId"] = env.PrincipalIdExpression,
                    ["principalName"] = principalName,
                    ["principalType"] = "ServicePrincipal",
                },
                ["options"] = new Dictionary<string, object?> { ["dependsOn"] = new List<object?>(dependsOn) },
            };
            dependsOn.Clear();
            dependsOn.Add($"${{{id}}}");
        }
    }

    /// <summary>Password authentication: a Key Vault holds the server and per-database connection strings; app identities may read them.</summary>
    private void AddKeyVaultAndSecrets(PulumiProgram program, string rg, string location, List<IPulumiManagedIdentityResource> environments)
    {
        var kv = $"{Id}Kv";
        program.AddKeyVault(kv, rg, location, name: NameFor(AzureNameKind.KeyVault, "kv"));
        ((Dictionary<string, object?>)((Dictionary<string, object?>)program.Resources[kv]!)["properties"]!)["tags"] =
            new Dictionary<string, object?> { ["aspire-resource-name"] = $"{Name}-kv" };
        program.Outputs[$"{Id}KeyVaultUri"] = $"${{{kv}.properties.vaultUri}}";
        program.Outputs[$"{Id}KeyVaultName"] = $"${{{kv}.name}}";

        var scope = $"${{{kv}.id}}";

        // The deploying principal needs data-plane write access on the RBAC-authorized vault before secrets can be created.
        var deployer = program.AddRoleAssignment($"{kv}Deployer", PulumiAzureProgramExtensions.CurrentPrincipalObjectId,
            KeyVaultSecretsOfficerRoleId, scope, "User");
        var secretOptions = new Dictionary<string, object?> { ["dependsOn"] = new List<object?> { $"${{{deployer}}}" } };

        var host = $"${{{Id}.fullyQualifiedDomainName}}";
        var serverCs = $"Host={host};Username=${{{UserNameConfigKey}}};Password=${{{PasswordConfigKey}}}";
        Provisioning.PulumiProgramExtensions.AddOutput(program, $"{Id}ConnectionString", serverCs, secret: true);

        var secretsToWrite = new List<(string Id, string SecretName, string Value)> { ($"{Id}ConnectionStringSecret", $"connectionstrings--{Name}", serverCs) };
        foreach (var db in Databases)
            secretsToWrite.Add((DatabaseSecretId(db), $"connectionstrings--{db.Name}", $"{serverCs};Database={db.DatabaseName}"));

        foreach (var (id, secretName, value) in secretsToWrite)
        {
            program.AddKeyVaultSecret(id, kv, rg, secretName, value);
            ((Dictionary<string, object?>)program.Resources[id]!)["options"] = new Dictionary<string, object?>(secretOptions);
            program.Outputs[$"{id}Uri"] = $"${{{id}.properties.secretUri}}";
        }

        foreach (var env in environments)
        {
            var envId = PulumiAzureProgramExtensions.ToPulumiId(env.Name);
            program.AddRoleAssignment($"{kv}{envId}SecretsUser", env.PrincipalIdExpression, KeyVaultSecretsUserRoleId, scope);
        }
    }
}

/// <summary>A database of an <see cref="AzurePostgresFlexibleServerResource"/> (connection string = the server's plus <c>Database=...</c>).</summary>
public sealed class AzurePostgresFlexibleServerDatabaseResource(string name, string databaseName, AzurePostgresFlexibleServerResource postgresParentResource)
    : Resource(name), IResourceWithParent<AzurePostgresFlexibleServerResource>, IResourceWithConnectionString
{
    public AzurePostgresFlexibleServerResource Parent { get; } = postgresParentResource;

    /// <summary>The database name on the server (may differ from the resource name).</summary>
    public string DatabaseName { get; } = databaseName;

    /// <summary>The matching database resource of the local container (set by <c>RunAsContainer</c>).</summary>
    public PostgresDatabaseResource? InnerResource { get; internal set; }

    internal string Id => PulumiAzureProgramExtensions.ToPulumiId(Name);

    public ReferenceExpression ConnectionStringExpression =>
        InnerResource is { } inner
            ? inner.ConnectionStringExpression
            : ReferenceExpression.Create($"{Parent.ConnectionStringExpression};Database={DatabaseName}");
}
