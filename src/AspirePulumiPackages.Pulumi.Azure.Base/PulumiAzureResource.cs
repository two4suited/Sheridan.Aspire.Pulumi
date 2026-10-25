using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;

namespace AspirePulumiPackages.Pulumi.Azure.Base;

/// <summary>
/// A resource whose Azure infrastructure is provisioned by a Pulumi stack (the equivalent of Aspire's
/// AzureProvisioningResource). Other Azure integrations derive from <see cref="PulumiAzureResource"/>.
/// </summary>
public interface IPulumiAzureResource : IPulumiProgramContributor
{
    new PulumiStackResource? Stack { get; set; }

    /// <summary>Azure region; when null the stack's 'location' config value is used.</summary>
    string? Location { get; set; }
}

/// <summary>
/// Base class for Pulumi-provisioned Azure resources: owns the stack binding, location override, Pulumi id and
/// output references, and (by default) its own resource group.
/// </summary>
public abstract class PulumiAzureResource(string name) : Resource(name), IPulumiAzureResource
{
    public PulumiStackResource? Stack { get; set; }

    public string? Location { get; set; }

    /// <summary>The name of an existing resource group to use instead of creating one (see <c>AsExisting</c>-style reuse).</summary>
    public string? ExistingResourceGroup { get; set; }

    /// <summary>A Pulumi-provisioned resource group (see <see cref="PulumiAzureExtensions.WithResourceGroup{T}"/>) to use instead of creating one.</summary>
    public PulumiAzureResourceGroupResource? ResourceGroup { get; set; }

    /// <summary>Pulumi YAML identifier derived from the resource name.</summary>
    public string Id => PulumiAzureProgramExtensions.ToPulumiId(Name);

    protected PulumiStackResource ResolveStack(PipelineStepContext ctx) =>
        Stack ?? ctx.Model.Resources.OfType<PulumiStackResource>().SingleOrDefault()
        ?? throw new InvalidOperationException($"Add a Pulumi stack with AddPulumiStack() (and bind '{Name}' with WithPulumiStack) to provision it.");

    /// <summary>Reference to a stack output named <c>{Id}{suffix}</c>, resolved lazily from the bound stack.</summary>
    protected PulumiOutputReference Output(PulumiStackResource stack, string suffix) => new(stack, $"{Id}{suffix}");

    public async Task ContributeAsync(PulumiProgram program, PipelineStepContext context)
    {
        var location = program.AddLocation(Location);
        string rg;
        if (ResourceGroup is { } group)
        {
            var stack = ResolveStack(context);
            if (group.Stack != stack)
                throw new InvalidOperationException(
                    $"'{Name}' and its resource group '{group.Name}' must be bound to the same Pulumi stack (call WithPulumiStack on both).");
            rg = group.NameExpression;
        }
        else if (ExistingResourceGroup is not null) rg = ExistingResourceGroup;
        else rg = program.AddResourceGroup(Id, location);

        await ContributeResourcesAsync(program, context, rg, location);
    }

    /// <summary>Adds this resource's own azure-native resources and outputs. <paramref name="resourceGroup"/> and <paramref name="location"/> are Pulumi references.</summary>
    protected abstract Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string resourceGroup, string location);
}
