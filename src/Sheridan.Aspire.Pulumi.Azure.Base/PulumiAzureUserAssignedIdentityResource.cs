using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;

namespace Sheridan.Aspire.Pulumi.Azure.Base;

/// <summary>A user-assigned managed identity provisioned by Pulumi (mirrors Aspire's AzureUserAssignedIdentityResource).</summary>
public sealed class PulumiAzureUserAssignedIdentityResource(string name) : PulumiAzureResource(name)
{
    protected override Task ContributeResourcesAsync(PulumiProgram program, PipelineStepContext context, string resourceGroup, string location)
    {
        program.AddUserAssignedIdentity(Id, resourceGroup, location, NameFor(AzureNameKind.UserAssignedIdentity));
        program.Outputs[$"{Id}Id"] = $"${{{Id}.id}}";
        program.Outputs[$"{Id}ClientId"] = $"${{{Id}.clientId}}";
        program.Outputs[$"{Id}PrincipalId"] = $"${{{Id}.principalId}}";
        program.Outputs[$"{Id}PrincipalName"] = $"${{{Id}.name}}";
        program.Outputs[$"{Id}ResourceGroup"] = resourceGroup;
        return Task.CompletedTask;
    }

    public PulumiOutputReference ClientId => new(Stack ?? throw Unbound(), $"{Id}ClientId");
    public PulumiOutputReference PrincipalId => new(Stack ?? throw Unbound(), $"{Id}PrincipalId");
    public PulumiOutputReference IdentityId => new(Stack ?? throw Unbound(), $"{Id}Id");

    private InvalidOperationException Unbound() => new($"'{Name}' has not been bound to a Pulumi stack; call WithPulumiStack().");
}
