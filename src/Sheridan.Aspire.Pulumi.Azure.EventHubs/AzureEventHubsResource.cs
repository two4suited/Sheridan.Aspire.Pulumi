using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Sheridan.Aspire.Pulumi.Azure.Base;
using PulumiExpr = Sheridan.Aspire.Pulumi.Provisioning.PulumiExpr;

namespace Sheridan.Aspire.Pulumi.Azure.EventHubs;

/// <summary>
/// An Azure Event Hubs namespace provisioned by Pulumi (azure-native), mirroring Aspire.Hosting.Azure.EventHubs's Bicep:
/// a Standard namespace with local (shared key) auth disabled, plus the event hubs and consumer groups added to the builder.
/// Compute environments hosting an app that references the namespace (or one of its hubs / consumer groups) are granted
/// Azure Event Hubs Data Owner by default (override with WithRoleAssignments).
/// </summary>
public sealed class AzureEventHubsResource(string name) : PulumiAzureResource(name), IResourceWithConnectionString
{
    internal static readonly AzureEventHubsRole[] DefaultRoles = [AzureEventHubsRole.AzureEventHubsDataOwner];

    /// <summary>Namespace SKU (default Standard, as in the official integration).</summary>
    public string Sku { get; set; } = "Standard";

    public List<AzureEventHubResource> Hubs { get; } = [];

    internal PulumiOutputReference OutputRef(string suffix) =>
        Output(Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack()."), suffix);

    /// <summary>The namespace's Service Bus endpoint, e.g. <c>https://ns.servicebus.windows.net:443/</c>.</summary>
    public PulumiOutputReference EventHubsEndpoint => OutputRef("EventHubsEndpoint");
    /// <summary>The namespace host name, e.g. <c>ns.servicebus.windows.net</c>.</summary>
    public PulumiOutputReference EventHubsHostName => OutputRef("EventHubsHostName");
    public PulumiOutputReference NameOutput => OutputRef("Name");

    public ReferenceExpression ConnectionStringExpression => ReferenceExpression.Create($"Endpoint={EventHubsEndpoint}");

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        // Named by the naming layer ({project}-{resource}-{stack}, normalised for EventHubsNamespace); override with WithAzureName.
        program.Resources[Id] = new Dictionary<string, object?>
        {
            ["type"] = "azure-native:eventhub:Namespace",
            ["properties"] = new Dictionary<string, object?>
            {
                ["namespaceName"] = NameFor(AzureNameKind.EventHubsNamespace),
                ["resourceGroupName"] = rg,
                ["location"] = location,
                ["disableLocalAuth"] = true,
                ["publicNetworkAccess"] = "Enabled",
                ["sku"] = new Dictionary<string, object?> { ["name"] = Sku },
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
            },
        };

        foreach (var hub in Hubs)
        {
            var props = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = rg,
                ["namespaceName"] = $"${{{Id}.name}}",
                ["eventHubName"] = hub.HubName,
            };
            if (hub.PartitionCount is { } pc) props["partitionCount"] = pc;
            program.Resources[$"{Id}{hub.Id}"] = new Dictionary<string, object?> { ["type"] = "azure-native:eventhub:EventHub", ["properties"] = props };

            foreach (var cg in hub.ConsumerGroups)
            {
                program.Resources[$"{Id}{hub.Id}{cg.Id}"] = new Dictionary<string, object?>
                {
                    ["type"] = "azure-native:eventhub:ConsumerGroup",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["resourceGroupName"] = rg,
                        ["namespaceName"] = $"${{{Id}.name}}",
                        ["eventHubName"] = $"${{{Id}{hub.Id}.name}}",
                        ["consumerGroupName"] = cg.ConsumerGroupName,
                    },
                };
            }
        }

        program.Outputs[$"{Id}EventHubsEndpoint"] = $"${{{Id}.serviceBusEndpoint}}";
        // split(replace(endpoint, 'https://', ''), ':')[0], expressed with Pulumi YAML functions.
        program.Outputs[$"{Id}EventHubsHostName"] = PulumiExpr.Select(0,
            PulumiExpr.Split(":", PulumiExpr.Select(1, PulumiExpr.Split("//", $"${{{Id}.serviceBusEndpoint}}"))));
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";

        AddRoleAssignments(program, context);
        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }

    private bool IsReferencedBy(IResource consumer)
    {
        var referenced = consumer.Annotations.OfType<ResourceRelationshipAnnotation>().Select(a => a.Resource).ToList();
        return referenced.Contains(this)
            || Hubs.Any(h => referenced.Contains(h) || h.ConsumerGroups.Any(referenced.Contains));
    }

    /// <summary>Grants each compute environment hosting a referencing app its roles on the namespace.</summary>
    private void AddRoleAssignments(PulumiProgram program, PipelineStepContext ctx)
    {
        var grants = new Dictionary<IPulumiManagedIdentityResource, HashSet<AzureEventHubsRole>>();
        foreach (var consumer in ctx.Model.GetComputeResources().Where(IsReferencedBy))
        {
            var custom = consumer.Annotations.OfType<AzureEventHubsRoleAnnotation>().Where(a => a.Target == this).ToList();
            IEnumerable<AzureEventHubsRole> roles = custom.Count > 0 ? custom.SelectMany(a => a.Roles) : DefaultRoles;
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
                    AzureEventHubsRoles.RoleDefinitionId(role), $"${{{Id}.id}}");
        }
    }
}

/// <summary>An event hub in an <see cref="AzureEventHubsResource"/> (connection string = namespace endpoint + <c>;EntityPath=</c>).</summary>
public sealed class AzureEventHubResource(string name, string hubName, AzureEventHubsResource parent) : Resource(name), IResourceWithConnectionString, IResourceWithParent<AzureEventHubsResource>
{
    public AzureEventHubsResource Parent { get; } = parent;
    public string HubName { get; set; } = hubName;
    /// <summary>Number of partitions (service default when null).</summary>
    public long? PartitionCount { get; set; }
    public List<AzureEventHubConsumerGroupResource> ConsumerGroups { get; } = [];
    internal string Id => PulumiAzureProgramExtensions.ToPulumiId(Name);

    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"{Parent.ConnectionStringExpression};EntityPath={HubName}");
}

/// <summary>A consumer group of an event hub (connection string = hub connection string + <c>;ConsumerGroup=</c>).</summary>
public sealed class AzureEventHubConsumerGroupResource(string name, string consumerGroupName, AzureEventHubResource parent) : Resource(name), IResourceWithConnectionString, IResourceWithParent<AzureEventHubResource>
{
    public AzureEventHubResource Parent { get; } = parent;
    public string ConsumerGroupName { get; } = consumerGroupName;
    internal string Id => PulumiAzureProgramExtensions.ToPulumiId(Name);

    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"{Parent.ConnectionStringExpression};ConsumerGroup={ConsumerGroupName}");
}

/// <summary>Roles the annotated resource's identity should get on a namespace (replaces the default).</summary>
internal sealed class AzureEventHubsRoleAnnotation(AzureEventHubsResource target, AzureEventHubsRole[] roles) : IResourceAnnotation
{
    public AzureEventHubsResource Target { get; } = target;
    public AzureEventHubsRole[] Roles { get; } = roles;
}
