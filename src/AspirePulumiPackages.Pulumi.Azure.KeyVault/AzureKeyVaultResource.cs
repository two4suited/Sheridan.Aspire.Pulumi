using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using AspirePulumiPackages.Pulumi.Azure.Base;

namespace AspirePulumiPackages.Pulumi.Azure.KeyVault;

/// <summary>
/// An Azure Key Vault provisioned by Pulumi (azure-native): an RBAC-enabled standard vault, its secrets, and role
/// assignments for the identities of compute environments hosting resources that reference it. Pulumi counterpart of
/// the official <c>AzureKeyVaultResource</c>; the vault URI is the connection string.
/// </summary>
public sealed class PulumiAzureKeyVaultResource(string name) : PulumiAzureResource(name), IResourceWithConnectionString
{
    /// <summary>Roles granted by default to the identity of anything that references the vault (same default as the official integration).</summary>
    internal static readonly AzureKeyVaultRole[] DefaultRoles = [AzureKeyVaultRole.KeyVaultAdministrator];

    /// <summary>Name of an existing vault (set by PublishAsExisting); when set, nothing is created for the vault itself.</summary>
    public string? ExistingVaultName { get; internal set; }

    internal List<PulumiAzureKeyVaultSecretResource> Secrets { get; } = [];

    /// <summary>Roles granted to the deploying principal (so Pulumi can write secrets under RBAC authorization).</summary>
    internal List<AzureKeyVaultRole> DeployerRoles { get; } = [];

    /// <summary>Output reference to the vault URI (https://name.vault.azure.net/).</summary>
    public PulumiOutputReference VaultUri => Output(Stack ?? throw Unbound(), "VaultUri");

    public PulumiOutputReference NameOutputReference => Output(Stack ?? throw Unbound(), "Name");

    public ReferenceExpression ConnectionStringExpression => ReferenceExpression.Create($"{VaultUri}");

    internal PulumiOutputReference SecretUri(string secretName) =>
        Output(Stack ?? throw Unbound(), $"Secret{PulumiAzureProgramExtensions.ToPulumiId(secretName)}Uri");

    private InvalidOperationException Unbound() => new($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack().");

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string resourceGroup, string location)
    {
        // Pulumi identifier of the vault (created) or of the lookup variable (existing); both expose name/id.
        string vault;
        if (ExistingVaultName is { } existing)
        {
            vault = $"{Id}Existing";
            program.Variables[vault] = new Dictionary<string, object?>
            {
                ["fn::invoke"] = new Dictionary<string, object?>
                {
                    ["function"] = "azure-native:keyvault:getVault",
                    ["arguments"] = new Dictionary<string, object?> { ["resourceGroupName"] = resourceGroup, ["vaultName"] = existing },
                },
            };
        }
        else
        {
            vault = Id;
            program.AddKeyVault(Id, resourceGroup, location, name: NameFor(AzureNameKind.KeyVault));
            ((Dictionary<string, object?>)((Dictionary<string, object?>)program.Resources[Id]!)["properties"]!)["tags"] =
                new Dictionary<string, object?> { ["aspire-resource-name"] = Name };
        }

        program.Outputs[$"{Id}VaultUri"] = $"${{{vault}.properties.vaultUri}}";
        program.Outputs[$"{Id}Name"] = $"${{{vault}.name}}";
        program.Outputs[$"{Id}Id"] = $"${{{vault}.id}}";

        var scope = $"${{{vault}.id}}";

        // Deployer roles come first so the secrets (written with the deploying principal's credentials) can depend on them.
        var deployerAssignments = new List<object?>();
        foreach (var role in DeployerRoles.Distinct())
        {
            var assignment = program.AddRoleAssignment($"{Id}Deployer{role}", PulumiAzureProgramExtensions.CurrentPrincipalObjectId,
                AzureKeyVaultRoles.RoleDefinitionId(role), scope, "User");
            deployerAssignments.Add($"${{{assignment}}}");
        }

        foreach (var secret in Secrets)
        {
            var secretId = $"{Id}Secret{PulumiAzureProgramExtensions.ToPulumiId(secret.Name)}";
            Provisioning.PulumiProgramExtensions.AddParameter(program, secret.ConfigKey, secret: true);
            program.AddKeyVaultSecret(secretId, vault, resourceGroup, secret.Name, $"${{{secret.ConfigKey}}}");
            if (deployerAssignments.Count > 0)
                ((Dictionary<string, object?>)program.Resources[secretId]!)["options"] =
                    new Dictionary<string, object?> { ["dependsOn"] = deployerAssignments };
            program.Outputs[$"{secretId}Uri"] = $"${{{secretId}.properties.secretUri}}";
        }

        AddComputeRoleAssignments(program, context, scope);
        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }

    /// <summary>Grants each compute environment's shared identity (for apps referencing the vault) its roles on the vault.</summary>
    private void AddComputeRoleAssignments(PulumiProgram program, PipelineStepContext ctx, string scope)
    {
        var grants = new Dictionary<IPulumiManagedIdentityResource, HashSet<AzureKeyVaultRole>>();
        foreach (var consumer in ctx.Model.GetComputeResources()
                     .Where(r => r.Annotations.OfType<ResourceRelationshipAnnotation>().Any(a => a.Resource == this)))
        {
            var custom = consumer.Annotations.OfType<AzureKeyVaultRoleAnnotation>().Where(a => a.Vault == this).ToList();
            IEnumerable<AzureKeyVaultRole> roles = custom.Count > 0 ? custom.SelectMany(a => a.Roles) : DefaultRoles;
            foreach (var env in consumer.Annotations.OfType<DeploymentTargetAnnotation>()
                         .Select(a => a.ComputeEnvironment).OfType<IPulumiManagedIdentityResource>())
            {
                if (!grants.TryGetValue(env, out var set)) grants[env] = set = [];
                set.UnionWith(roles);
            }
        }

        foreach (var (env, roles) in grants)
        {
            var envId = PulumiAzureProgramExtensions.ToPulumiId(env.Name);
            foreach (var role in roles)
                program.AddRoleAssignment($"{Id}{envId}{role}", env.PrincipalIdExpression,
                    AzureKeyVaultRoles.RoleDefinitionId(role), scope);
        }
    }
}

/// <summary>A secret in a <see cref="PulumiAzureKeyVaultResource"/>; the value comes from a Pulumi config entry.</summary>
public sealed class PulumiAzureKeyVaultSecretResource(string name, string configKey)
{
    public string Name { get; } = name;

    /// <summary>Pulumi config key (secret) holding the value.</summary>
    internal string ConfigKey { get; } = configKey;
}

/// <summary>Roles the annotated resource's identity should get on a vault (replaces the default).</summary>
internal sealed class AzureKeyVaultRoleAnnotation(PulumiAzureKeyVaultResource vault, AzureKeyVaultRole[] roles) : IResourceAnnotation
{
    public PulumiAzureKeyVaultResource Vault { get; } = vault;
    public AzureKeyVaultRole[] Roles { get; } = roles;
}
