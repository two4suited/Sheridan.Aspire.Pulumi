using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using AspirePulumiPackages.Pulumi.Azure.Base;

namespace AspirePulumiPackages.Pulumi.Azure.SignalR;

/// <summary>
/// An Azure SignalR Service provisioned by Pulumi (azure-native), mirroring Aspire.Hosting.Azure.SignalR's Bicep:
/// <c>Microsoft.SignalRService/signalR</c> of kind SignalR, SKU Free_F1 (capacity 1), local auth disabled, CORS allowing
/// any origin and the <c>ServiceMode</c> feature. Compute environments hosting an app that references the service are
/// granted SignalR App Server by default (override with WithRoleAssignments).
/// </summary>
public sealed class AzureSignalRResource(string name) : PulumiAzureResource(name), IResourceWithConnectionString
{
    internal static readonly AzureSignalRRole[] DefaultRoles = [AzureSignalRRole.SignalRAppServer];

    public AzureSignalRServiceMode ServiceMode { get; set; } = AzureSignalRServiceMode.Default;
    /// <summary>SKU name (default Free_F1, as in the official integration).</summary>
    public string Sku { get; set; } = "Free_F1";
    public int Capacity { get; set; } = 1;

    internal PulumiOutputReference OutputRef(string suffix) =>
        Output(Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack()."), suffix);

    /// <summary>The service host name, e.g. <c>sig-xyz.service.signalr.net</c>.</summary>
    public PulumiOutputReference HostName => OutputRef("HostName");
    public PulumiOutputReference NameOutput => OutputRef("Name");

    /// <summary>The service URI, <c>https://{HostName}</c>.</summary>
    public ReferenceExpression UriExpression => ReferenceExpression.Create($"https://{HostName}");

    public ReferenceExpression ConnectionStringExpression => ReferenceExpression.Create($"Endpoint=https://{HostName};AuthType=azure");

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        // Named by the naming layer ({project}-{resource}-{stack}, normalised for SignalRService); override with WithAzureName.
        // An infra target: role assignments are created in the targeted provision pass and depend on the service.
        program.AddInfraResource(Id, "azure-native:signalrservice:SignalR", new Dictionary<string, object?>
        {
            ["type"] = "azure-native:signalrservice:SignalR",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceName"] = NameFor(AzureNameKind.SignalRService),
                ["resourceGroupName"] = rg,
                ["location"] = location,
                ["kind"] = "SignalR",
                ["sku"] = new Dictionary<string, object?> { ["name"] = Sku, ["capacity"] = Capacity },
                ["cors"] = new Dictionary<string, object?> { ["allowedOrigins"] = new List<object?> { "*" } },
                ["disableLocalAuth"] = true,
                ["features"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["flag"] = "ServiceMode", ["value"] = ServiceMode.ToString() },
                },
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
            },
        });

        program.Outputs[$"{Id}HostName"] = $"${{{Id}.hostName}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";

        AddRoleAssignments(program, context);
        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }

    /// <summary>Grants each compute environment hosting a referencing app its roles on the service.</summary>
    private void AddRoleAssignments(PulumiProgram program, PipelineStepContext ctx)
    {
        var grants = new Dictionary<IPulumiManagedIdentityResource, HashSet<AzureSignalRRole>>();
        foreach (var consumer in ctx.Model.GetComputeResources())
        {
            if (!consumer.Annotations.OfType<ResourceRelationshipAnnotation>().Any(a => a.Resource == this)) continue;
            var custom = consumer.Annotations.OfType<AzureSignalRRoleAnnotation>().Where(a => a.Target == this).ToList();
            IEnumerable<AzureSignalRRole> roles = custom.Count > 0 ? custom.SelectMany(a => a.Roles) : DefaultRoles;
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
                    AzureSignalRRoles.RoleDefinitionId(role), $"${{{Id}.id}}");
        }
    }
}

/// <summary>Roles the annotated resource's identity should get on a service (replaces the default).</summary>
internal sealed class AzureSignalRRoleAnnotation(AzureSignalRResource target, AzureSignalRRole[] roles) : IResourceAnnotation
{
    public AzureSignalRResource Target { get; } = target;
    public AzureSignalRRole[] Roles { get; } = roles;
}
