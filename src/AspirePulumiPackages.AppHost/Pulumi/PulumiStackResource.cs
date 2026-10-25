using Aspire.Hosting.ApplicationModel;

namespace AspirePulumiPackages.AppHost.Pulumi;

public sealed class PulumiStackResource(string name, string stackName) : Resource(name)
{
    public string StackName { get; } = stackName;
    public string? Organization { get; set; }
    public string? EscEnvironment { get; set; }

    public string ProjectName => Name;

    public string QualifiedStack => Organization is null ? StackName : $"{Organization}/{ProjectName}/{StackName}";

    // 'project/environment' form, as required by the stack's `environment:` import.
    public string StackEscReference => EscEnvironment ?? $"{ProjectName}/{StackName}";

    public string QualifiedEscEnvironment => Organization is null ? StackEscReference : $"{Organization}/{StackEscReference}";
}
