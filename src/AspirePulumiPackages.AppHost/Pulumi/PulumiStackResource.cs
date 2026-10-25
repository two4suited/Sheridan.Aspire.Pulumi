using Aspire.Hosting.ApplicationModel;

namespace AspirePulumiPackages.AppHost.Pulumi;

public sealed class PulumiStackResource(string name, string stackName) : Resource(name)
{
    public string StackName { get; } = stackName;
    public string? Organization { get; set; }
    public List<string> EscEnvironments { get; } = [];

    public bool EscFromConfiguration { get; set; }

    public string ProjectName => Name;

    public string QualifiedStack => Organization is null ? StackName : $"{Organization}/{ProjectName}/{StackName}";

    // Imported in order; later environments override earlier ones. Each is in 'project/environment' form.
    public IReadOnlyList<string> StackEscReferences => EscEnvironments.Count > 0 ? EscEnvironments : [$"{ProjectName}/{StackName}"];

    // Only the implicit default environment is created on deploy; explicit ones must already exist.
    public bool UsesDefaultEscEnvironment => EscEnvironments.Count == 0;

    public string QualifiedDefaultEscEnvironment => Organization is null ? $"{ProjectName}/{StackName}" : $"{Organization}/{ProjectName}/{StackName}";
}
