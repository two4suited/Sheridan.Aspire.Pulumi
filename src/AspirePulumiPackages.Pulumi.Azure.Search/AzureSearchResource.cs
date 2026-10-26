using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using AspirePulumiPackages.Pulumi.Azure.Base;
using PulumiExpr = AspirePulumiPackages.Pulumi.Provisioning.PulumiExpr;

namespace AspirePulumiPackages.Pulumi.Azure.Search;

/// <summary>
/// An Azure AI Search service provisioned by Pulumi (azure-native), mirroring Aspire.Hosting.Azure.Search's Bicep:
/// a basic-SKU service (1 partition, 1 replica) with local (key) auth disabled. Compute environments hosting an app that
/// references the service are granted Search Index Data Contributor and Search Service Contributor by default
/// (override with WithRoleAssignments).
/// </summary>
public sealed class AzureSearchResource(string name) : PulumiAzureResource(name), IResourceWithConnectionString
{
    internal static readonly AzureSearchRole[] DefaultRoles = [AzureSearchRole.SearchIndexDataContributor, AzureSearchRole.SearchServiceContributor];

    /// <summary>Service SKU (default basic, as in the official integration).</summary>
    public string Sku { get; set; } = "basic";

    internal PulumiOutputReference OutputRef(string suffix) =>
        Output(Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack()."), suffix);

    /// <summary>The service endpoint, e.g. <c>https://svc.search.windows.net</c>.</summary>
    public PulumiOutputReference Endpoint => OutputRef("Endpoint");
    public PulumiOutputReference NameOutput => OutputRef("Name");

    public ReferenceExpression ConnectionStringExpression => ReferenceExpression.Create($"Endpoint={Endpoint}");

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        // No searchServiceName: the provider auto-names it (<id> + random suffix), the equivalent of Bicep's
        // take('<name>-${uniqueString(resourceGroup().id)}', 60).
        program.Resources[Id] = new Dictionary<string, object?>
        {
            ["type"] = "azure-native:search:Service",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = rg,
                ["location"] = location,
                ["hostingMode"] = "default",
                ["disableLocalAuth"] = true,
                ["partitionCount"] = 1,
                ["replicaCount"] = 1,
                ["sku"] = new Dictionary<string, object?> { ["name"] = Sku },
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
            },
        };

        // 'https://${search.name}.search.windows.net'
        program.Outputs[$"{Id}Endpoint"] = PulumiExpr.Join("", "https://", $"${{{Id}.name}}", ".search.windows.net");
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";

        AddRoleAssignments(program, context);
        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }

    /// <summary>Grants each compute environment hosting a referencing app its roles on the service.</summary>
    private void AddRoleAssignments(PulumiProgram program, PipelineStepContext ctx)
    {
        var grants = new Dictionary<IPulumiManagedIdentityResource, HashSet<AzureSearchRole>>();
        foreach (var consumer in ctx.Model.GetComputeResources()
                     .Where(c => c.Annotations.OfType<ResourceRelationshipAnnotation>().Any(a => a.Resource == this)))
        {
            var custom = consumer.Annotations.OfType<AzureSearchRoleAnnotation>().Where(a => a.Target == this).ToList();
            IEnumerable<AzureSearchRole> roles = custom.Count > 0 ? custom.SelectMany(a => a.Roles) : DefaultRoles;
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
                    AzureSearchRoles.RoleDefinitionId(role), $"${{{Id}.id}}");
        }
    }
}

/// <summary>Roles the annotated resource's identity should get on a service (replaces the default).</summary>
internal sealed class AzureSearchRoleAnnotation(AzureSearchResource target, AzureSearchRole[] roles) : IResourceAnnotation
{
    public AzureSearchResource Target { get; } = target;
    public AzureSearchRole[] Roles { get; } = roles;
}
