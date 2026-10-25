using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;

namespace AspirePulumiPackages.AppHost.Pulumi;

/// <summary>An Azure AD (Entra ID) app registration and service principal provisioned by Pulumi (azuread).</summary>
public sealed class AzureAdAppRegistrationResource(string name) : Resource(name), IPulumiProgramContributor
{
    public PulumiStackResource? Stack { get; set; }

    /// <summary>Display name of the app registration; defaults to the resource name.</summary>
    public string? DisplayName { get; set; }

    private string Id => string.Concat(
        Name.Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select((p, i) => i == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));

    public PulumiOutputReference ClientId => Output("ClientId");
    public PulumiOutputReference ObjectId => Output("ObjectId");
    public PulumiOutputReference ServicePrincipalObjectId => Output("ServicePrincipalObjectId");

    private PulumiOutputReference Output(string suffix) =>
        new(Stack ?? throw new InvalidOperationException($"'{Name}' has not been bound to a Pulumi stack yet."), $"{Id}{suffix}");

    public Task ContributeAsync(PulumiProgram program, PipelineStepContext ctx)
    {
        program.Resources[$"{Id}App"] = new Dictionary<string, object?>
        {
            ["type"] = "azuread:index/application:Application",
            ["properties"] = new Dictionary<string, object?>
            {
                ["displayName"] = DisplayName ?? Name,
            },
        };

        program.Resources[$"{Id}Sp"] = new Dictionary<string, object?>
        {
            ["type"] = "azuread:index/servicePrincipal:ServicePrincipal",
            ["properties"] = new Dictionary<string, object?>
            {
                ["clientId"] = $"${{{Id}App.clientId}}",
            },
        };

        program.Outputs[$"{Id}ClientId"] = $"${{{Id}App.clientId}}";
        program.Outputs[$"{Id}ObjectId"] = $"${{{Id}App.objectId}}";
        program.Outputs[$"{Id}ServicePrincipalObjectId"] = $"${{{Id}Sp.objectId}}";

        return Task.CompletedTask;
    }
}
