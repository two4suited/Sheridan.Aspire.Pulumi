using Aspire.Hosting.ApplicationModel;

namespace Sheridan.Aspire.Pulumi;

public sealed class PulumiStackResource(string name, string stackName) : Resource(name)
{
    public string StackName { get; } = stackName;
    public string? Organization { get; set; }
    public List<string> EscEnvironments { get; } = [];

    public bool EscFromConfiguration { get; set; }

    /// <summary>Set when the project is written; used to read stack outputs.</summary>
    public string? ProjectDirectory { get; set; }

    /// <summary>Logical names/types of the resources of every infrastructure (non-app) resource of the generated program; the provision pass creates exactly these.</summary>
    public List<(string Type, string Name)> InfraTargets { get; } = [];

    /// <summary>
    /// The program the provision pass runs instead of the full one, or null when they are the same. It leaves out the
    /// infrastructure that depends on an app (see <see cref="PulumiProgram.ToProvisionYaml"/>), which the deploy pass creates.
    /// </summary>
    public string? ProvisionProgramYaml { get; set; }

    public bool HasApps { get; set; }

    /// <summary>
    /// True once the provision pass has created the stack's infrastructure in this run. Until then, stack outputs of
    /// not-yet-created resources are expected to be missing (the program is regenerated afterwards), so deferrals are
    /// only worth a warning once this is set.
    /// </summary>
    public bool InfrastructureProvisioned { get; set; }

    /// <summary>Image tag shared by every image pushed in this deployment.</summary>
    public string DeployTag { get; } = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

    /// <summary>Pulumi project name; defaults to the resource name, but can be overridden so multiple stacks share one project.</summary>
    public string? ProjectNameOverride { get; set; }

    public string ProjectName => ProjectNameOverride ?? Name;

    public string QualifiedStack => Organization is null ? StackName : $"{Organization}/{ProjectName}/{StackName}";

    // Imported in order; later environments override earlier ones. Each is in 'project/environment' form.
    public IReadOnlyList<string> StackEscReferences => EscEnvironments.Count > 0 ? EscEnvironments : [$"{ProjectName}/{StackName}"];

    // Only the implicit default environment is created on deploy; explicit ones must already exist.
    public bool UsesDefaultEscEnvironment => EscEnvironments.Count == 0;

    public string QualifiedDefaultEscEnvironment => Organization is null ? $"{ProjectName}/{StackName}" : $"{Organization}/{ProjectName}/{StackName}";
}
