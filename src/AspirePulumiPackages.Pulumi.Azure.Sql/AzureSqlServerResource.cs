using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using AspirePulumiPackages.Pulumi.Azure.Base;

namespace AspirePulumiPackages.Pulumi.Azure.Sql;

/// <summary>
/// An Azure SQL logical server provisioned by Pulumi (azure-native), mirroring the Bicep of the official
/// <c>AzureSqlServerResource</c>: Entra-only authentication with a user-assigned admin identity, TLS 1.2, the
/// "AllowAllAzureIps" firewall rule, free-tier serverless databases (GP_S_Gen5_2), and a deployment script per
/// database that creates a contained user for every compute environment identity hosting an app that references the
/// server or database and adds it to db_owner. The connection string is passwordless (Active Directory Default).
/// </summary>
public sealed class AzureSqlServerResource(string name) : PulumiAzureResource(name), IResourceWithConnectionString
{
    /// <summary>Name of an existing server (set by PublishAsExisting); when set, no server or admin identity is created.</summary>
    public string? ExistingServerName { get; internal set; }

    public List<AzureSqlDatabaseResource> Databases { get; } = [];
    public List<AzureSqlElasticPoolResource> ElasticPools { get; } = [];
    internal List<(string Name, string Start, string End)> FirewallRules { get; } = [];

    // Pulumi autonaming keeps the logical name's case, but SQL server names must be lowercase.
    internal string ServerId => Id.ToLowerInvariant() == Id ? Id : $"{Id.ToLowerInvariant()}Server";
    private string AdminId => $"{Id}AdminIdentity";

    private PulumiStackResource BoundStack => Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack().");

    /// <summary>Output reference to the server's fully qualified domain name.</summary>
    public PulumiOutputReference HostName => Output(BoundStack, "SqlServerFqdn");

    public PulumiOutputReference NameOutputReference => Output(BoundStack, "Name");

    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"Server=tcp:{HostName},1433;Encrypt=True;Authentication=\"Active Directory Default\"");

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        program.AddClientConfig();
        string server;
        if (ExistingServerName is { } existing)
        {
            server = $"{Id}Existing";
            program.Variables[server] = new Dictionary<string, object?>
            {
                ["fn::invoke"] = new Dictionary<string, object?>
                {
                    ["function"] = "azure-native:sql:getServer",
                    ["arguments"] = new Dictionary<string, object?> { ["resourceGroupName"] = rg, ["serverName"] = existing },
                },
            };
            program.Outputs[$"{Id}SqlServerFqdn"] = $"${{{server}.fullyQualifiedDomainName}}";
        }
        else
        {
            server = ServerId;
            program.AddInfraResource(AdminId, "azure-native:managedidentity:UserAssignedIdentity", new()
            {
                ["type"] = "azure-native:managedidentity:UserAssignedIdentity",
                ["properties"] = new Dictionary<string, object?> { ["resourceGroupName"] = rg, ["location"] = location },
            });
            program.AddInfraResource(server, "azure-native:sql:Server", new()
            {
                ["type"] = "azure-native:sql:Server",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["location"] = location,
                    ["version"] = "12.0",
                    ["minimalTlsVersion"] = "1.2",
                    ["publicNetworkAccess"] = "Enabled",
                    ["administrators"] = new Dictionary<string, object?>
                    {
                        ["administratorType"] = "ActiveDirectory",
                        ["login"] = $"${{{AdminId}.name}}",
                        ["sid"] = $"${{{AdminId}.principalId}}",
                        ["tenantId"] = PulumiAzureProgramExtensions.TenantId,
                        ["azureADOnlyAuthentication"] = true,
                    },
                    ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
                },
            });
            program.Outputs[$"{Id}SqlServerFqdn"] = $"${{{server}.fullyQualifiedDomainName}}";
            program.Outputs[$"{Id}SqlServerAdminName"] = $"${{{AdminId}.name}}";
        }

        program.Outputs[$"{Id}Name"] = $"${{{server}.name}}";
        program.Outputs[$"{Id}Id"] = $"${{{server}.id}}";

        Dictionary<string, object?> Child(string type, Dictionary<string, object?> extra)
        {
            var props = new Dictionary<string, object?> { ["resourceGroupName"] = rg, ["serverName"] = $"${{{server}.name}}" };
            foreach (var (k, v) in extra) props[k] = v;
            return new() { ["type"] = type, ["properties"] = props };
        }

        // The default rule that lets Azure services (e.g. Container Apps) reach the server, as in the official Bicep.
        var firewallIds = new List<object?>();
        var rules = new List<(string Name, string Start, string End)> { ("AllowAllAzureIps", "0.0.0.0", "0.0.0.0") };
        rules.AddRange(FirewallRules);
        foreach (var (ruleName, start, end) in rules)
        {
            var ruleId = $"{Id}FirewallRule{PulumiAzureProgramExtensions.ToPulumiId(ruleName)}";
            program.Resources[ruleId] = Child("azure-native:sql:FirewallRule",
                new() { ["firewallRuleName"] = ruleName, ["startIpAddress"] = start, ["endIpAddress"] = end });
            firewallIds.Add($"${{{ruleId}}}");
        }

        foreach (var pool in ElasticPools)
        {
            var d = Child("azure-native:sql:ElasticPool", new()
            {
                ["elasticPoolName"] = pool.PoolName,
                ["location"] = location,
                ["sku"] = new Dictionary<string, object?> { ["name"] = pool.Sku },
            });
            program.Resources[$"{Id}{pool.Id}"] = d;
        }

        foreach (var db in Databases)
        {
            var props = new Dictionary<string, object?> { ["databaseName"] = db.DatabaseName, ["location"] = location };
            if (db.ElasticPool is { } pool)
            {
                props["elasticPoolId"] = $"${{{Id}{pool.Id}.id}}";
                props["sku"] = new Dictionary<string, object?> { ["name"] = "ElasticPool", ["tier"] = "GeneralPurpose" };
            }
            else
            {
                // Free-tier serverless database, as in the official Bicep.
                props["sku"] = new Dictionary<string, object?> { ["name"] = "GP_S_Gen5_2" };
                props["useFreeLimit"] = true;
                props["freeLimitExhaustionBehavior"] = "AutoPause";
            }
            program.Resources[$"{Id}{db.Id}"] = Child("azure-native:sql:Database", props);
        }

        if (ExistingServerName is null) AddDatabaseRoleScripts(program, context, rg, location, server, firewallIds);
        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }

    /// <summary>
    /// For each compute environment hosting an app that references the server (all databases) or a database, runs an
    /// AzurePowerShell deployment script (as the admin identity) that creates the environment identity's contained
    /// user and adds it to db_owner, like the official "sql-roles" Bicep module.
    /// </summary>
    private void AddDatabaseRoleScripts(PulumiProgram program, PipelineStepContext ctx, string rg, string location, string server, List<object?> firewallIds)
    {
        var wanted = new HashSet<(IPulumiManagedIdentityResource Env, AzureSqlDatabaseResource Db)>();
        foreach (var app in ctx.Model.GetComputeResources())
        {
            var referenced = app.Annotations.OfType<ResourceRelationshipAnnotation>().Select(a => a.Resource).ToList();
            foreach (var env in app.Annotations.OfType<DeploymentTargetAnnotation>()
                         .Select(a => a.ComputeEnvironment).OfType<IPulumiManagedIdentityResource>())
            {
                if (env.PrincipalNameExpression is null || env.ClientIdExpression is null) continue;
                foreach (var db in Databases)
                    if (referenced.Contains(this) || referenced.Contains(db)) wanted.Add((env, db));
            }
        }

        foreach (var (env, db) in wanted)
        {
            var envId = PulumiAzureProgramExtensions.ToPulumiId(env.Name);
            var dependsOn = new List<object?>(firewallIds) { $"${{{Id}{db.Id}}}" };
            program.Resources[$"{Id}{db.Id}{envId}Roles"] = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:resources:AzurePowerShellScript",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["location"] = location,
                    ["kind"] = "AzurePowerShell",
                    ["azPowerShellVersion"] = "14.0",
                    ["retentionInterval"] = "PT1H",
                    ["identity"] = new Dictionary<string, object?>
                    {
                        ["type"] = "UserAssigned",
                        ["userAssignedIdentities"] = new List<object?> { $"${{{AdminId}.id}}" },
                    },
                    ["environmentVariables"] = new List<object?>
                    {
                        EnvVar("DBNAME", db.DatabaseName),
                        EnvVar("DBSERVER", $"${{{server}.fullyQualifiedDomainName}}"),
                        EnvVar("PRINCIPALTYPE", "ServicePrincipal"),
                        EnvVar("PRINCIPALNAME", env.PrincipalNameExpression),
                        EnvVar("ID", env.ClientIdExpression),
                    },
                    ["scriptContent"] = RoleScript,
                },
                ["options"] = new Dictionary<string, object?> { ["dependsOn"] = dependsOn },
            };
        }

        static Dictionary<string, object?> EnvVar(string name, string value) =>
            new() { ["name"] = name, ["value"] = value };
    }

    // Condensed from the official sql-roles.bicep script: same T-SQL (stale-SID reconciliation, idempotent CREATE USER,
    // db_owner membership in one transaction) executed with System.Data.SqlClient and a managed identity token.
    // Must not contain a dollar-brace sequence (Pulumi YAML interpolation).
    private const string RoleScript = """
$sqlServerFqdn = "$env:DBSERVER"
$sqlDatabaseName = "$env:DBNAME"
$principalName = "$env:PRINCIPALNAME"
$id = "$env:ID"
$escapedPrincipalName = $principalName.Replace("'", "''")

$sqlCmd = @"
DECLARE @name SYSNAME = '$escapedPrincipalName';
DECLARE @id UNIQUEIDENTIFIER = '$id';
DECLARE @sid VARBINARY(16) = CONVERT(VARBINARY(16), @id);
DECLARE @castId NVARCHAR(MAX) = CONVERT(VARCHAR(MAX), @sid, 1);
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @existingSid VARBINARY(85) = (SELECT sid FROM sys.database_principals WHERE name = @name AND type = 'E');
IF @existingSid IS NOT NULL AND @existingSid <> @sid
BEGIN
    DECLARE @dropCmd NVARCHAR(MAX) = N'DROP USER ' + QUOTENAME(@name);
    EXEC (@dropCmd);
    SET @existingSid = NULL;
END
IF @existingSid IS NULL
BEGIN
    DECLARE @cmd NVARCHAR(MAX) = N'CREATE USER ' + QUOTENAME(@name) + N' WITH SID = ' + @castId + N', TYPE = E;';
    EXEC (@cmd);
END
DECLARE @role1 NVARCHAR(MAX) = N'ALTER ROLE db_owner ADD MEMBER ' + QUOTENAME(@name);
EXEC (@role1);
COMMIT TRANSACTION;
"@

Write-Host $sqlCmd

$sqlDnsSuffix = (Get-AzContext).Environment.SqlDatabaseDnsSuffix
if ([string]::IsNullOrWhiteSpace($sqlDnsSuffix)) { $sqlDnsSuffix = ".database.windows.net" }
$sqlAudience = "https://" + $sqlDnsSuffix.TrimStart('.') + "/"
$connectionString = "Server=tcp:$($sqlServerFqdn),1433;Initial Catalog=$($sqlDatabaseName);Encrypt=True;TrustServerCertificate=False;"

$maxRetries = 5
$retryDelay = 60
$attempt = 0
$success = $false
while (-not $success -and $attempt -lt $maxRetries) {
    $attempt++
    Write-Host "Attempt $attempt of $maxRetries..."
    $connection = $null
    try {
        $tokenResponse = Get-AzAccessToken -ResourceUrl $sqlAudience
        $accessToken = if ($tokenResponse.Token -is [System.Security.SecureString]) {
            [System.Net.NetworkCredential]::new("", $tokenResponse.Token).Password
        } else { $tokenResponse.Token }
        $connection = New-Object System.Data.SqlClient.SqlConnection
        $connection.ConnectionString = $connectionString
        $connection.AccessToken = $accessToken
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $sqlCmd
        [void]$command.ExecuteNonQuery()
        $success = $true
        Write-Host "SQL command succeeded on attempt $attempt."
    } catch {
        Write-Host "Attempt $attempt failed: $_"
        if ($attempt -lt $maxRetries) { Start-Sleep -Seconds $retryDelay } else { throw }
    } finally {
        if ($null -ne $connection) { $connection.Dispose() }
    }
}
""";
}

/// <summary>A database on an <see cref="AzureSqlServerResource"/>; its connection string is the server's plus <c>Database=</c>.</summary>
public sealed class AzureSqlDatabaseResource(string name, string databaseName, AzureSqlServerResource parent)
    : Resource(name), IResourceWithParent<AzureSqlServerResource>, IResourceWithConnectionString
{
    public AzureSqlServerResource Parent { get; } = parent;

    /// <summary>The database name in SQL Server (defaults to the resource name).</summary>
    public string DatabaseName { get; } = databaseName;

    /// <summary>The elastic pool this database belongs to, if any (otherwise a free-tier serverless database).</summary>
    public AzureSqlElasticPoolResource? ElasticPool { get; internal set; }

    internal string Id => PulumiAzureProgramExtensions.ToPulumiId(Name);

    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"{Parent.ConnectionStringExpression};Database={DatabaseName}");
}

/// <summary>An elastic pool on an <see cref="AzureSqlServerResource"/> that databases can be placed in.</summary>
public sealed class AzureSqlElasticPoolResource(string name, string poolName, string sku, AzureSqlServerResource parent)
    : Resource(name), IResourceWithParent<AzureSqlServerResource>
{
    public AzureSqlServerResource Parent { get; } = parent;
    public string PoolName { get; } = poolName;

    /// <summary>Pool SKU name, e.g. GP_Gen5_2 (General Purpose, Gen5, 2 vCores).</summary>
    public string Sku { get; } = sku;

    internal string Id => PulumiAzureProgramExtensions.ToPulumiId(Name);
}
