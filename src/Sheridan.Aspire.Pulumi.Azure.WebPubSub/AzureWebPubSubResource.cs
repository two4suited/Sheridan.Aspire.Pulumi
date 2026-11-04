using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using Sheridan.Aspire.Pulumi.Azure.Base;

namespace Sheridan.Aspire.Pulumi.Azure.WebPubSub;

/// <summary>
/// An Azure Web PubSub service provisioned by Pulumi (azure-native), mirroring Aspire.Hosting.Azure.WebPubSub's Bicep:
/// a Free_F1 (capacity 1) service with local auth disabled, plus the hubs (and their event handlers) added to the builder.
/// Compute environments hosting an app that references the service (or one of its hubs) are granted
/// Web PubSub Service Owner by default (override with WithRoleAssignments).
/// </summary>
public sealed class AzureWebPubSubResource(string name) : PulumiAzureResource(name), IResourceWithConnectionString
{
    internal static readonly AzureWebPubSubRole[] DefaultRoles = [AzureWebPubSubRole.WebPubSubServiceOwner];

    /// <summary>SKU name (default Free_F1, as in the official integration).</summary>
    public string Sku { get; set; } = "Free_F1";
    /// <summary>SKU capacity / unit count (default 1).</summary>
    public int Capacity { get; set; } = 1;

    public List<AzureWebPubSubHubResource> Hubs { get; } = [];

    internal PulumiOutputReference OutputRef(string suffix) =>
        Output(Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack()."), suffix);

    /// <summary>The service endpoint, e.g. <c>https://svc.webpubsub.azure.com</c>.</summary>
    public PulumiOutputReference Endpoint => OutputRef("Endpoint");
    public PulumiOutputReference NameOutput => OutputRef("Name");

    public ReferenceExpression ConnectionStringExpression => ReferenceExpression.Create($"Endpoint={Endpoint}");

    protected override async Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string rg, string location)
    {
        // Named by the naming layer ({project}-{resource}-{stack}, normalised for WebPubSubService); override with WithAzureName.
        program.Resources[Id] = new Dictionary<string, object?>
        {
            ["type"] = "azure-native:webpubsub:WebPubSub",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceName"] = NameFor(AzureNameKind.WebPubSubService),
                ["resourceGroupName"] = rg,
                ["location"] = location,
                ["disableLocalAuth"] = true,
                ["sku"] = new Dictionary<string, object?> { ["name"] = Sku, ["capacity"] = Capacity },
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
            },
        };

        foreach (var hub in Hubs)
        {
            var props = new Dictionary<string, object?>();
            if (hub.EventHandlers.Count > 0)
            {
                var handlers = new List<object?>();
                foreach (var h in hub.EventHandlers)
                {
                    var handler = new Dictionary<string, object?>
                    {
                        ["urlTemplate"] = await h.UrlTemplate.GetValueAsync(context.CancellationToken).ConfigureAwait(false),
                    };
                    if (h.UserEventPattern is not null) handler["userEventPattern"] = h.UserEventPattern;
                    if (h.SystemEvents is { Length: > 0 }) handler["systemEvents"] = h.SystemEvents.Cast<object?>().ToList();
                    handlers.Add(handler);
                }
                props["eventHandlers"] = handlers;
            }
            program.Resources[$"{Id}{hub.Id}"] = new Dictionary<string, object?>
            {
                ["type"] = "azure-native:webpubsub:WebPubSubHub",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = rg,
                    ["resourceName"] = $"${{{Id}.name}}",
                    ["hubName"] = hub.HubName,
                    ["properties"] = props,
                },
            };
        }

        // 'https://${wps.properties.hostName}'
        program.Outputs[$"{Id}Endpoint"] = $"https://${{{Id}.hostName}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";

        AddRoleAssignments(program, context);
        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
    }

    private bool IsReferencedBy(IResource consumer)
    {
        var referenced = consumer.Annotations.OfType<ResourceRelationshipAnnotation>().Select(a => a.Resource).ToList();
        return referenced.Contains(this) || Hubs.Any(h => referenced.Contains(h));
    }

    /// <summary>Grants each compute environment hosting a referencing app its roles on the service.</summary>
    private void AddRoleAssignments(PulumiProgram program, PipelineStepContext ctx)
    {
        var grants = new Dictionary<IPulumiManagedIdentityResource, HashSet<AzureWebPubSubRole>>();
        foreach (var consumer in ctx.Model.GetComputeResources().Where(IsReferencedBy))
        {
            var custom = consumer.Annotations.OfType<AzureWebPubSubRoleAnnotation>().Where(a => a.Target == this).ToList();
            IEnumerable<AzureWebPubSubRole> roles = custom.Count > 0 ? custom.SelectMany(a => a.Roles) : DefaultRoles;
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
                    AzureWebPubSubRoles.RoleDefinitionId(role), $"${{{Id}.id}}");
        }
    }
}

/// <summary>An event handler of a hub (upstream <c>WebPubSubEventHandler</c>).</summary>
public sealed record AzureWebPubSubEventHandler(ReferenceExpression UrlTemplate, string? UserEventPattern, string[]? SystemEvents);

/// <summary>A hub in an <see cref="AzureWebPubSubResource"/>.</summary>
public sealed class AzureWebPubSubHubResource(string name, string hubName, AzureWebPubSubResource parent) : Resource(name), IResourceWithConnectionString, IResourceWithParent<AzureWebPubSubResource>
{
    public AzureWebPubSubResource Parent { get; } = parent;
    public string HubName { get; set; } = hubName;
    public List<AzureWebPubSubEventHandler> EventHandlers { get; } = [];
    internal string Id => PulumiAzureProgramExtensions.ToPulumiId(Name);

    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"{Parent.ConnectionStringExpression};Hub={HubName}");
}

/// <summary>Roles the annotated resource's identity should get on a service (replaces the default).</summary>
internal sealed class AzureWebPubSubRoleAnnotation(AzureWebPubSubResource target, AzureWebPubSubRole[] roles) : IResourceAnnotation
{
    public AzureWebPubSubResource Target { get; } = target;
    public AzureWebPubSubRole[] Roles { get; } = roles;
}
