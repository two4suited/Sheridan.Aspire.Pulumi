using Aspire.Hosting.Pipelines;
using AspirePulumiPackages.Pulumi.Azure.Base;

namespace AspirePulumiPackages.Pulumi.Azure.OperationalInsights;

/// <summary>
/// A Log Analytics workspace provisioned by Pulumi (azure-native). Mirrors <c>AzureLogAnalyticsWorkspaceResource</c>
/// from <c>Aspire.Hosting.Azure.OperationalInsights</c>: a PerGB2018 workspace tagged with
/// <c>aspire-resource-name</c>, exposing the workspace id and name as outputs.
/// </summary>
public sealed class LogAnalyticsWorkspaceResource(string name) : PulumiAzureResource(name)
{
    /// <summary>Output reference for the workspace resource id (Bicep output <c>logAnalyticsWorkspaceId</c>).</summary>
    public PulumiOutputReference LogAnalyticsWorkspaceId => new(RequireStack(), $"{Id}LogAnalyticsWorkspaceId");

    /// <summary>Output reference for the workspace name (Bicep output <c>name</c>).</summary>
    public PulumiOutputReference NameOutputReference => new(RequireStack(), $"{Id}Name");

    private PulumiStackResource RequireStack() =>
        Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack; call WithPulumiStack().");

    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string resourceGroup, string location)
    {
        program.AddInfraResource(Id, "azure-native:operationalinsights:Workspace", new()
        {
            ["type"] = "azure-native:operationalinsights:Workspace",
            ["properties"] = new Dictionary<string, object?>
            {
                ["workspaceName"] = NameFor(AzureNameKind.LogAnalyticsWorkspace),
                ["resourceGroupName"] = resourceGroup,
                ["location"] = location,
                ["sku"] = new Dictionary<string, object?> { ["name"] = "PerGB2018" },
                ["tags"] = new Dictionary<string, object?> { ["aspire-resource-name"] = Name },
            },
        });

        program.Outputs[$"{Id}LogAnalyticsWorkspaceId"] = $"${{{Id}.id}}";
        program.Outputs[$"{Id}Name"] = $"${{{Id}.name}}";

        Provisioning.PulumiInfrastructureExtensions.ApplyInfrastructureCallbacks(this, program);
        return Task.CompletedTask;
    }
}
