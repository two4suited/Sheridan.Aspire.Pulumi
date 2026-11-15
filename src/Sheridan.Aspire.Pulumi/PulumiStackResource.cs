using Aspire.Hosting.ApplicationModel;

namespace Sheridan.Aspire.Pulumi;

public sealed class PulumiStackResource(string name, string stackName) : Resource(name)
{
    public string StackName { get; } = stackName;
    public string? Organization { get; set; }
    public List<string> EscEnvironments { get; } = [];

    public bool EscFromConfiguration { get; set; }

    /// <summary>Explicit local backend URL (<c>file://...</c>) set by <c>WithLocalBackend</c>; passed to the CLI as PULUMI_BACKEND_URL.</summary>
    public string? LocalBackendUrl { get; set; }

    /// <summary>Plain (non-ESC) stack config written to <c>Pulumi.&lt;stack&gt;.yaml</c>, e.g. <c>azure-native:metadataHost</c>.</summary>
    public Dictionary<string, string> StackConfig { get; } = [];

    /// <summary>
    /// Secret stack config (<c>WithConfig(..., secret: true)</c>). Never written to <c>Pulumi.&lt;stack&gt;.yaml</c> as plaintext;
    /// applied with <c>pulumi config set --secret</c> so Pulumi stores it encrypted.
    /// </summary>
    public Dictionary<string, string> StackSecretConfig { get; } = [];

    /// <summary>The backend URL in effect (explicit, PULUMI_BACKEND_URL, or the current login), or null when unknown.</summary>
    public string? BackendUrl => _backendUrl ??= PulumiBackend.Resolve(LocalBackendUrl) ?? "";  // resolved once so every step agrees
    private string? _backendUrl;

    /// <summary>Per-process environment for this stack's <c>pulumi</c> calls (never mutates the AppHost's own environment).</summary>
    public IReadOnlyDictionary<string, string>? CliEnvironment =>
        LocalBackendUrl is null ? null : new Dictionary<string, string> { ["PULUMI_BACKEND_URL"] = LocalBackendUrl };

    /// <summary>True when the backend is self-managed (not Pulumi Cloud): no <c>whoami</c> check, no ESC.</summary>
    public bool UsesLocalBackend => PulumiBackend.IsLocalUrl(BackendUrl);

    /// <summary>Set when the project is written; used to read stack outputs.</summary>
    public string? ProjectDirectory { get; set; }

    /// <summary>Logical names/types of the resources of every infrastructure (non-app) resource of the generated program; the provision pass creates exactly these.</summary>
    public List<(string Type, string Name)> InfraTargets { get; } = [];

    /// <summary>
    /// The program the provision pass runs instead of the full one, or null when they are the same. It leaves out the
    /// infrastructure that depends on an app (see <see cref="PulumiProgram.ToProvisionYaml"/>), which the deploy pass creates.
    /// </summary>
    public string? ProvisionProgramYaml { get; set; }

    /// <summary>Infrastructure left out of the provision program because it references an app (see <see cref="PulumiProgram.GetDeferredResources"/>).</summary>
    public List<(string Type, string Name)> DeferredTargets { get; } = [];

    /// <summary>Logical names of the app resources of the generated program.</summary>
    public List<string> AppResourceNames { get; } = [];

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

    public string QualifiedStack => UsesLocalBackend || Organization is null ? StackName : $"{Organization}/{ProjectName}/{StackName}";

    // Imported in order; later environments override earlier ones. Each is in 'project/environment' form.
    public IReadOnlyList<string> StackEscReferences => EscEnvironments.Count > 0 ? EscEnvironments : [$"{ProjectName}/{StackName}"];

    // Only the implicit default environment is created on deploy; explicit ones must already exist.
    public bool UsesDefaultEscEnvironment => EscEnvironments.Count == 0;

    public string QualifiedDefaultEscEnvironment => Organization is null ? $"{ProjectName}/{StackName}" : $"{Organization}/{ProjectName}/{StackName}";
}
