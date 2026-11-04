using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Sheridan.Aspire.Pulumi.Azure.Base;

namespace Sheridan.Aspire.Pulumi.Azure.AppConfiguration;

/// <summary>
/// An Azure App Configuration store provisioned by Pulumi (azure-native), mirroring Aspire.Hosting.Azure.AppConfiguration's
/// Bicep: a standard-SKU store with local (access key) auth disabled, tagged <c>aspire-resource-name</c>, with outputs
/// <c>appConfigEndpoint</c>, <c>name</c> and <c>id</c>. Compute environments hosting an app that references the store are
/// granted App Configuration Data Owner by default (override with WithRoleAssignments). The endpoint is the connection string.
/// </summary>
public sealed class AzureAppConfigurationResource(string name) : PulumiAzureResource(name), IResourceWithConnectionString
{
    internal static readonly AzureAppConfigurationRole[] DefaultRoles = [AzureAppConfigurationRole.AppConfigurationDataOwner];

    /// <summary>Name of an existing store (set by PublishAsExisting); when set, nothing is created for the store itself.</summary>
    public string? ExistingStoreName { get; internal set; }

    /// <summary>The local emulator container, when <c>RunAsEmulator</c> was called (run mode only).</summary>
    public AzureAppConfigurationEmulatorResource? Emulator { get; internal set; }

    internal PulumiOutputReference OutputRef(string suffix) =>
        Output(Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack()."), suffix);

    /// <summary>The store endpoint, e.g. <c>https://name.azconfig.io</c>.</summary>
    public PulumiOutputReference AppConfigEndpoint => OutputRef("AppConfigEndpoint");

    public PulumiOutputReference NameOutput => OutputRef("Name");

    public ReferenceExpression ConnectionStringExpression => Emulator is { } e
        ? ReferenceExpression.Create($"Endpoint={e.GetEndpoint(AzureAppConfigurationEmulatorResource.EndpointName)};Id=anonymous;Secret=abcdefghijklmnopqrstuvwxyz1234567890;Anonymous=True")
        : ReferenceExpression.Create($"{AppConfigEndpoint}");

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        // Pulumi identifier of the store (created) or of the lookup variable (existing); both expose name/id.
        string store;
        if (ExistingStoreName is { } existing)
        {
            store = $"{Id}Existing";
            program.Variables[store] = new Dictionary<string, object?>
            {
                ["fn::invoke"] = new Dictionary<string, object?>
                {
                    ["function"] = "azure-native:appconfiguration:getConfigurationStore",
                    ["arguments"] = new Dictionary<string, object?> { ["resourceGroupName"] = rg, ["configStoreName"] = existing },
                },
            };
        }
        else
        {
            store = Id;
            // Named by the naming layer ({project}-{resource}-{stack}, normalised for AppConfigurationStore); override with WithAzureName.
            program.Resources[Id] = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:appconfiguration:ConfigurationStore",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["configStoreName"] = NameFor(AzureNameKind.AppConfigurationStore),
                    ["resourceGroupName"] = rg,
                    ["location"] = location,
                    ["disableLocalAuth"] = true,
                    ["sku"] = new Dictionary<string, object?> { ["name"] = "standard" },
                    ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
                },
            };
        }

        program.Outputs[$"{Id}AppConfigEndpoint"] = $"${{{store}.endpoint}}";
        program.Outputs[$"{Id}Name"] = $"${{{store}.name}}";
        program.Outputs[$"{Id}Id"] = $"${{{store}.id}}";

        AddRoleAssignments(program, context, $"${{{store}.id}}");
        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }

    /// <summary>Grants each compute environment hosting a referencing app its roles on the store.</summary>
    private void AddRoleAssignments(PulumiProgram program, PipelineStepContext ctx, string scope)
    {
        var grants = new Dictionary<IPulumiManagedIdentityResource, HashSet<AzureAppConfigurationRole>>();
        foreach (var consumer in ctx.Model.GetComputeResources()
                     .Where(r => r.Annotations.OfType<ResourceRelationshipAnnotation>().Any(a => a.Resource == this)))
        {
            var custom = consumer.Annotations.OfType<AzureAppConfigurationRoleAnnotation>().Where(a => a.Target == this).ToList();
            IEnumerable<AzureAppConfigurationRole> roles = custom.Count > 0 ? custom.SelectMany(a => a.Roles) : DefaultRoles;
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
                    AzureAppConfigurationRoles.RoleDefinitionId(role), scope);
        }
    }
}

/// <summary>The local App Configuration emulator container (run mode only).</summary>
public sealed class AzureAppConfigurationEmulatorResource(AzureAppConfigurationResource innerResource)
    : ContainerResource(innerResource.Name + "-emulator")
{
    internal const string EndpointName = "emulator";

    public AzureAppConfigurationResource InnerResource { get; } = innerResource;
}

/// <summary>Roles the annotated resource's identity should get on a store (replaces the default).</summary>
internal sealed class AzureAppConfigurationRoleAnnotation(AzureAppConfigurationResource target, AzureAppConfigurationRole[] roles) : IResourceAnnotation
{
    public AzureAppConfigurationResource Target { get; } = target;
    public AzureAppConfigurationRole[] Roles { get; } = roles;
}
