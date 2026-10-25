using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;

namespace AspirePulumiPackages.Pulumi.Azure.Base;

/// <summary>
/// A standalone Azure resource group provisioned by Pulumi, shared by any number of <see cref="PulumiAzureResource"/>s
/// via <see cref="PulumiAzureExtensions.WithResourceGroup{T}"/> instead of each one creating its own.
/// </summary>
public sealed class PulumiAzureResourceGroupResource(string name) : Resource(name), IPulumiAzureResource
{
    public PulumiStackResource? Stack { get; set; }

    public string? Location { get; set; }

    public string Id => PulumiAzureProgramExtensions.ToPulumiId(Name);

    /// <summary>Pulumi reference to this resource group's name (e.g. <c>${sharedRg.name}</c>); only valid within the stack it's provisioned in.</summary>
    public string NameExpression => $"${{{Id}.name}}";

    public Task ContributeAsync(PulumiProgram program, PipelineStepContext context)
    {
        var location = program.AddLocation(Location);
        program.AddInfraResource(Id, "azure-native:resources:ResourceGroup", new()
        {
            ["type"] = "azure-native:resources:ResourceGroup",
            ["properties"] = new Dictionary<string, object?> { ["location"] = location },
        });
        program.Outputs[$"{Id}Name"] = NameExpression;
        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";
        return Task.CompletedTask;
    }
}
