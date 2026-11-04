namespace Sheridan.Aspire.Pulumi.Azure.Base;

/// <summary>
/// Shared azure-native building blocks for contributors: the Pulumi equivalent of the shared
/// provisioning context (subscription / location / resource group) and role-assignment helpers
/// in Aspire.Hosting.Azure. Every returned string is a Pulumi YAML interpolation reference.
/// </summary>
public static class PulumiAzureProgramExtensions
{
    /// <summary>Converts an Aspire resource name (my-res) to a Pulumi YAML identifier (myRes).</summary>
    public static string ToPulumiId(string name) => string.Concat(
        name.Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select((p, i) => i == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));

    /// <summary>Declares the shared 'location' config value (default 'eastus'); returns the reference to use for resources.</summary>
    public static string AddLocation(this PulumiProgram program, string? location = null, string defaultLocation = "eastus")
    {
        program.Configuration.TryAdd("location", new Dictionary<string, object?> { ["type"] = "string", ["default"] = defaultLocation });
        return location ?? "${location}";
    }

    /// <summary>Adds the azure-native client config lookup (subscription / tenant / object id) once; idempotent.</summary>
    public static void AddClientConfig(this PulumiProgram program)
    {
        program.Variables.TryAdd("clientConfig", new Dictionary<string, object?>
        {
            ["fn::invoke"] = new Dictionary<string, object?>
            {
                ["function"] = "azure-native:authorization:getClientConfig",
                ["arguments"] = new Dictionary<string, object?>(),
            },
        });
    }

    public const string SubscriptionId = "${clientConfig.subscriptionId}";
    public const string TenantId = "${clientConfig.tenantId}";
    public const string CurrentPrincipalObjectId = "${clientConfig.objectId}";

    /// <summary>Adds a resource group (the equivalent of Aspire's per-environment resource group) and returns its name reference.</summary>
    public static string AddResourceGroup(this PulumiProgram program, string id, string location, string? name = null)
    {
        var properties = new Dictionary<string, object?> { ["location"] = location };
        if (name is not null) properties["resourceGroupName"] = AzureNaming.ValidateOverride(AzureNameKind.ResourceGroup, name);
        program.AddInfraResource($"{id}Rg", "azure-native:resources:ResourceGroup", new()
        {
            ["type"] = "azure-native:resources:ResourceGroup",
            ["properties"] = properties,
        });
        return $"${{{id}Rg.name}}";
    }

    /// <summary>Adds a user-assigned managed identity; returns the resource id (e.g. 'myIdentity') usable as <c>${id.principalId}</c>.</summary>
    public static void AddUserAssignedIdentity(this PulumiProgram program, string id, string resourceGroup, string location, string? name = null)
    {
        var properties = new Dictionary<string, object?> { ["resourceGroupName"] = resourceGroup, ["location"] = location };
        if (name is not null) properties["resourceName"] = AzureNaming.ValidateOverride(AzureNameKind.UserAssignedIdentity, name);
        program.AddInfraResource(id, "azure-native:managedidentity:UserAssignedIdentity", new()
        {
            ["type"] = "azure-native:managedidentity:UserAssignedIdentity",
            ["properties"] = properties,
        });
    }

    /// <summary>
    /// Assigns a built-in role. <paramref name="principalId"/> and <paramref name="scope"/> are Pulumi references
    /// (e.g. <c>${identity.principalId}</c>, <c>${registry.id}</c>); returns the assignment's resource name for dependsOn.
    /// </summary>
    public static string AddRoleAssignment(this PulumiProgram program, string name, string principalId, string roleDefinitionId,
        string scope, string principalType = "ServicePrincipal")
    {
        program.AddClientConfig();
        program.AddInfraResource(name, "azure-native:authorization:RoleAssignment", new()
        {
            ["type"] = "azure-native:authorization:RoleAssignment",
            ["properties"] = new Dictionary<string, object?>
            {
                ["principalId"] = principalId,
                ["principalType"] = principalType,
                ["roleDefinitionId"] = $"/subscriptions/{SubscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{roleDefinitionId}",
                ["scope"] = scope,
            },
        });
        return name;
    }

    /// <summary>
    /// Adds an RBAC-enabled Key Vault (the shared secret store Aspire provisions for secret outputs). When <paramref name="name"/> is given (see <see cref="AzureNaming"/> / <c>NameFor</c>) it is validated and written as <c>vaultName</c>; otherwise the provider auto-names the vault.
    /// </summary>
    public static void AddKeyVault(this PulumiProgram program, string id, string resourceGroup, string location, string sku = "standard", string? name = null)
    {
        program.AddClientConfig();
        var properties = new Dictionary<string, object?>
        {
            ["resourceGroupName"] = resourceGroup,
            ["location"] = location,
            ["properties"] = new Dictionary<string, object?>
            {
                ["tenantId"] = TenantId,
                ["sku"] = new Dictionary<string, object?> { ["family"] = "A", ["name"] = sku },
                ["enableRbacAuthorization"] = true,
            },
        };
        if (name is not null) properties["vaultName"] = AzureNaming.ValidateOverride(AzureNameKind.KeyVault, name);
        program.AddInfraResource(id, "azure-native:keyvault:Vault", new()
        {
            ["type"] = "azure-native:keyvault:Vault",
            ["properties"] = properties,
        });
    }

    /// <summary>Adds a secret to a Key Vault; returns the secret's resource name.</summary>
    public static string AddKeyVaultSecret(this PulumiProgram program, string name, string vaultId, string resourceGroup, string secretName, string value)
    {
        program.AddInfraResource(name, "azure-native:keyvault:Secret", new()
        {
            ["type"] = "azure-native:keyvault:Secret",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = resourceGroup,
                ["vaultName"] = $"${{{vaultId}.name}}",
                ["secretName"] = AzureNaming.ValidateOverride(AzureNameKind.KeyVaultSecret, secretName, nameof(secretName)),
                ["properties"] = new Dictionary<string, object?> { ["value"] = value },
            },
        });
        return name;
    }
}
