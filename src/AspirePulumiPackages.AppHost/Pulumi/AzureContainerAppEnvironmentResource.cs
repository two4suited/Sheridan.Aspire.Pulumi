using Aspire.Hosting.ApplicationModel;

namespace AspirePulumiPackages.AppHost.Pulumi;

/// <summary>
/// An Azure Container Apps environment provisioned by Pulumi (azure-native): resource group,
/// Log Analytics workspace and managed environment.
/// </summary>
public sealed class AzureContainerAppEnvironmentResource(string name) : Resource(name), IPulumiProgramContributor
{
    public PulumiStackResource? Stack { get; set; }

    /// <summary>Azure region; when null, the stack's 'location' config value (default 'eastus') is used.</summary>
    public string? Location { get; set; }

    private string Id => string.Concat(
        Name.Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select((p, i) => i == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));

    public void Contribute(PulumiProgram program)
    {
        var location = Location ?? "${location}";
        program.Configuration.TryAdd("location", new Dictionary<string, object?> { ["type"] = "string", ["default"] = "eastus" });

        program.Resources[$"{Id}Rg"] = new Dictionary<string, object?>
        {
            ["type"] = "azure-native:resources:ResourceGroup",
            ["properties"] = new Dictionary<string, object?> { ["location"] = location },
        };

        program.Resources[$"{Id}Logs"] = new Dictionary<string, object?>
        {
            ["type"] = "azure-native:operationalinsights:Workspace",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = $"${{{Id}Rg.name}}",
                ["location"] = location,
                ["retentionInDays"] = 30,
                ["sku"] = new Dictionary<string, object?> { ["name"] = "PerGB2018" },
            },
        };

        program.Variables[$"{Id}LogKeys"] = new Dictionary<string, object?>
        {
            ["fn::invoke"] = new Dictionary<string, object?>
            {
                ["function"] = "azure-native:operationalinsights:getSharedKeys",
                ["arguments"] = new Dictionary<string, object?>
                {
                    ["resourceGroupName"] = $"${{{Id}Rg.name}}",
                    ["workspaceName"] = $"${{{Id}Logs.name}}",
                },
            },
        };

        program.Resources[Id] = new Dictionary<string, object?>
        {
            ["type"] = "azure-native:app:ManagedEnvironment",
            ["properties"] = new Dictionary<string, object?>
            {
                ["resourceGroupName"] = $"${{{Id}Rg.name}}",
                ["location"] = location,
                ["appLogsConfiguration"] = new Dictionary<string, object?>
                {
                    ["destination"] = "log-analytics",
                    ["logAnalyticsConfiguration"] = new Dictionary<string, object?>
                    {
                        ["customerId"] = $"${{{Id}Logs.customerId}}",
                        ["sharedKey"] = $"${{{Id}LogKeys.primarySharedKey}}",
                    },
                },
            },
        };

        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";
        program.Outputs[$"{Id}DefaultDomain"] = $"${{{Id}.defaultDomain}}";
        program.Outputs[$"{Id}ResourceGroup"] = $"${{{Id}Rg.name}}";
    }
}
