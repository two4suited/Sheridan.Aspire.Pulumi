namespace AspirePulumiPackages.Pulumi.Provisioning;

/// <summary>Shared building blocks used by Azure.Provisioning-style overlays (identities, role assignments, parameters, outputs).</summary>
public static class PulumiProgramExtensions
{
    /// <summary>Adds an azure-native user-assigned managed identity (Aspire's UserAssignedIdentity).</summary>
    public static void AddUserAssignedIdentity(this PulumiProgram program, string name, string resourceGroupName, string? location = null) =>
        new PulumiResourceBuilder("azure-native:managedidentity:UserAssignedIdentity")
            .InResourceGroup(resourceGroupName, location)
            .AddTo(program, name);

    /// <summary>
    /// Adds an azure-native role assignment (Aspire's RoleAssignment). <paramref name="roleDefinitionGuid"/> is a built-in role GUID;
    /// requires the "clientConfig" variable, see <see cref="AddClientConfig"/> (called automatically).
    /// </summary>
    public static void AddRoleAssignment(this PulumiProgram program, string name, string scopeResourceName, string principalIdExpression,
        string roleDefinitionGuid, string principalType = "ServicePrincipal")
    {
        program.AddClientConfig();
        new PulumiResourceBuilder("azure-native:authorization:RoleAssignment")
            .Property("principalId", principalIdExpression)
            .Property("principalType", principalType)
            .Property("roleDefinitionId", $"/subscriptions/{PulumiExpr.Ref("clientConfig", "subscriptionId")}/providers/Microsoft.Authorization/roleDefinitions/{roleDefinitionGuid}")
            .Property("scope", PulumiExpr.Ref(scopeResourceName, "id"))
            .AddTo(program, name);
    }

    /// <summary>Adds the "clientConfig" variable (azure-native:authorization:getClientConfig); idempotent.</summary>
    public static void AddClientConfig(this PulumiProgram program) =>
        program.Variables.TryAdd("clientConfig", PulumiExpr.Invoke("azure-native:authorization:getClientConfig"));

    /// <summary>Adds a program output (Aspire's BicepOutput); optionally wrapped as a secret.</summary>
    public static void AddOutput(this PulumiProgram program, string name, object? value, bool secret = false) =>
        program.Outputs[name] = secret ? PulumiExpr.Secret(value) : value;

    /// <summary>Declares a config parameter with an optional default (Aspire's BicepParameter).</summary>
    public static void AddParameter(this PulumiProgram program, string name, string type = "string", object? defaultValue = null, bool secret = false)
    {
        var def = new Dictionary<string, object?> { ["type"] = type };
        if (defaultValue is not null) def["default"] = defaultValue;
        if (secret) def["secret"] = true;
        program.Configuration[name] = def;
    }
}
