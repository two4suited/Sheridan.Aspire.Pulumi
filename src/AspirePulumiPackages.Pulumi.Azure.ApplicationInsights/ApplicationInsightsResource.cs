using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;
using AspirePulumiPackages.Pulumi.Azure.Base;
using AspirePulumiPackages.Pulumi.Azure.OperationalInsights;

namespace AspirePulumiPackages.Pulumi.Azure.ApplicationInsights;

/// <summary>
/// An Application Insights component provisioned by Pulumi (azure-native). Mirrors <c>AzureApplicationInsightsResource</c>
/// from <c>Aspire.Hosting.Azure.ApplicationInsights</c>: a <c>web</c> component backed by a Log Analytics workspace
/// (an implicit PerGB2018 workspace in the same program unless <see cref="LogAnalyticsWorkspace"/> is set), whose
/// connection string is exposed as the resource's connection string.
/// </summary>
public sealed class PulumiApplicationInsightsResource(string name) : PulumiAzureResource(name), IResourceWithConnectionString
{
    /// <summary>The Log Analytics workspace to link to; when null a workspace is created alongside the component.</summary>
    public LogAnalyticsWorkspaceResource? LogAnalyticsWorkspace { get; set; }

    /// <summary>Output reference for the connection string (Bicep output <c>appInsightsConnectionString</c>).</summary>
    public PulumiOutputReference ConnectionString => Output(Stack ?? throw Unbound(), "AppInsightsConnectionString");

    /// <summary>Output reference for the component name (Bicep output <c>name</c>).</summary>
    public PulumiOutputReference NameOutputReference => Output(Stack ?? throw Unbound(), "Name");

    /// <summary>Output reference for the component resource id (Bicep output <c>id</c>).</summary>
    public PulumiOutputReference IdOutputReference => Output(Stack ?? throw Unbound(), "Id");

    public ReferenceExpression ConnectionStringExpression => ReferenceExpression.Create($"{ConnectionString}");

    private InvalidOperationException Unbound() => new($"'{Name}' has not been bound to a Pulumi stack yet; call WithPulumiStack().");

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string resourceGroup, string location)
    {
        string workspaceId;
        if (LogAnalyticsWorkspace is { } law)
        {
            // The workspace is a Pulumi resource in this same program, so it can only be linked within one stack.
            var stack = ResolveStack(context);
            var lawStack = law.Stack ?? context.Model.Resources.OfType<PulumiStackResource>().SingleOrDefault();
            if (lawStack != stack)
                throw new InvalidOperationException(
                    $"'{Name}' and its Log Analytics workspace '{law.Name}' must be bound to the same Pulumi stack.");
            workspaceId = $"${{{law.Id}.id}}";
        }
        else
        {
            var lawId = $"{Id}Law";
            program.AddInfraResource(lawId, "azure-native:operationalinsights:Workspace", new()
            {
                ["type"] = "azure-native:operationalinsights:Workspace",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = resourceGroup,
                    ["location"] = location,
                    ["sku"] = new Dictionary<string, object?> { ["name"] = "PerGB2018" },
                    ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = $"law_{Name}" },
                },
            });
            workspaceId = $"${{{lawId}.id}}";
        }

        program.AddInfraResource(Id, "azure-native:applicationinsights:Component", new()
        {
            ["type"] = "azure-native:applicationinsights:Component",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = resourceGroup,
                ["location"] = location,
                ["kind"] = "web",
                ["applicationType"] = "web",
                ["workspaceResourceId"] = workspaceId,
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
            },
        });

        program.Outputs[$"{Id}AppInsightsConnectionString"] = $"${{{Id}.connectionString}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";
        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";
        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }
}
