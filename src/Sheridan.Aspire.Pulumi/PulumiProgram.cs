using System.Text;
using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;

namespace Sheridan.Aspire.Pulumi;

/// <summary>A resource that adds Pulumi YAML resources to a stack's generated program.</summary>
public interface IPulumiProgramContributor : IResource
{
    /// <summary>The stack this contributes to; null means every stack in the AppHost.</summary>
    PulumiStackResource? Stack { get; }

    Task ContributeAsync(PulumiProgram program, PipelineStepContext context);
}

public sealed class PulumiProgram
{
    public Dictionary<string, object?> Configuration { get; } = [];
    public Dictionary<string, object?> Variables { get; } = [];
    public Dictionary<string, object?> Resources { get; } = [];
    public Dictionary<string, object?> Outputs { get; } = [];

    /// <summary>
    /// Whether the program has a compute half (apps, workloads, anything that needs images pushed or that consumes
    /// infrastructure outputs), so the pipeline needs a provision pass before the deploy pass.
    /// </summary>
    public bool HasApps { get; set; }

    private readonly HashSet<string> _appResources = [];
    private readonly HashSet<string> _explicitInfra = [];

    /// <summary>Names of resources marked as app (compute) resources, before the transitive-dependents rule of <see cref="GetInfrastructureTargets"/>.</summary>
    public IReadOnlyCollection<string> AppResources => _appResources;

    /// <summary>Adds a resource that belongs to the app half of the program (container app, web app, workload, ...). Everything not marked as an app is infrastructure.</summary>
    public void AddAppResource(string name, Dictionary<string, object?> definition)
    {
        Resources[name] = definition;
        _appResources.Add(name);
        _explicitInfra.Remove(name);
        HasApps = true;
    }

    /// <summary>Marks already-added resources as apps.</summary>
    public void MarkAsApps(IEnumerable<string> names)
    {
        foreach (var n in names)
            if (!_explicitInfra.Contains(n)) _appResources.Add(n);
        HasApps = true;
    }

    /// <summary>
    /// Every resource added to the program while the returned scope is open is an app resource, except those added with
    /// <see cref="AddInfraResource"/>. Used by compute environments that emit a whole workload half (e.g. Kubernetes).
    /// </summary>
    public IDisposable BeginAppScope() => new AppScope(this);

    private sealed class AppScope : IDisposable
    {
        private readonly PulumiProgram _program;
        private readonly HashSet<string> _before;

        public AppScope(PulumiProgram program)
        {
            _program = program;
            _before = [.. program.Resources.Keys];
        }

        public void Dispose() => _program.MarkAsApps(_program.Resources.Keys.Where(k => !_before.Contains(k)));
    }

    /// <summary>Adds a resource explicitly marked as infrastructure (the default for anything not added through <see cref="AddAppResource"/>).</summary>
    public void AddInfraResource(string name, string type, Dictionary<string, object?> definition)
    {
        Resources[name] = definition;
        _explicitInfra.Add(name);
        _appResources.Remove(name);
    }

    /// <summary>
    /// The resources a provision pass must create so that every output apps depend on exists before the apps are
    /// generated: all resources except the apps, and except any resource that (transitively) references an app,
    /// since targeting it would drag the app in.
    /// </summary>
    public List<(string Type, string Name)> GetInfrastructureTargets()
    {
        var apps = AppSide();
        return Resources
            .Where(kv => !apps.Contains(kv.Key) && kv.Value is IDictionary<string, object?> d && d.ContainsKey("type"))
            .Select(kv => (Type: (string)((IDictionary<string, object?>)kv.Value!)["type"]!, Name: kv.Key))
            .ToList();
    }

    /// <summary>The apps plus every resource and variable that (transitively, also through a variable) references one.</summary>
    private HashSet<string> AppSide()
    {
        var apps = new HashSet<string>(_appResources);
        Close(apps, Resources, Variables);
        return apps;
    }

    /// <summary>Grows <paramref name="names"/> with every entry of <paramref name="maps"/> that (transitively) references one of them.</summary>
    private static void Close(HashSet<string> names, params Dictionary<string, object?>[] maps)
    {
        var refs = maps.SelectMany(m => m).Select(kv => (Name: kv.Key, Deps: ReferencedNames(kv.Value))).ToList();
        bool changed;
        do
        {
            changed = false;
            foreach (var (name, deps) in refs)
                if (!names.Contains(name) && deps.Overlaps(names)) { names.Add(name); changed = true; }
        } while (changed);
    }

    /// <summary>
    /// The infrastructure resources that are left to the deploy pass because they (transitively) reference an app, e.g.
    /// an API Management backend whose URL is a container app's ingress FQDN, and the policy ordered after that backend.
    /// </summary>
    public IReadOnlyCollection<string> GetDeferredResources()
    {
        var deferred = AppSide();
        deferred.ExceptWith(_appResources);
        deferred.IntersectWith(Resources.Keys);
        return deferred;
    }

    /// <summary>
    /// The program for the provision pass, or null when it is the same as <see cref="ToYaml(string, string)"/>.
    /// <para>
    /// Not targeting a resource is not enough to keep it out of the provision pass: Pulumi YAML still evaluates every
    /// resource of the program, and an app that is skipped (not targeted, not created yet) has no outputs, so a nested
    /// access such as <c>${apiApp.configuration.ingress.fqdn}</c> fails the whole update with "receiver must be a list
    /// or object, not nil". The provision program therefore leaves out the deferred resources
    /// (<see cref="GetDeferredResources"/>) and whatever references them (resources, variables, outputs). The update is
    /// targeted, so resources that are missing from the program are not deleted; the deploy pass runs the full program.
    /// </para>
    /// </summary>
    public string? ToProvisionYaml(string name, string description)
    {
        // Deferred resources, and variables that read an app.
        var omitted = AppSide();
        omitted.ExceptWith(_appResources);
        if (omitted.Count == 0) return null;
        Close(omitted, Resources, Variables);

        Dictionary<string, object?> Keep(Dictionary<string, object?> map, bool byValue = false) => map
            .Where(kv => !omitted.Contains(kv.Key) && !(byValue && ReferencedNames(kv.Value).Overlaps(omitted)))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        return ToYaml(name, description, Keep(Variables), Keep(Resources), Keep(Outputs, byValue: true));
    }

    private static readonly System.Text.RegularExpressions.Regex InterpolationRef =
        new(@"(?<!\$)\$\{\s*([A-Za-z0-9_-]+)", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The root names of every <c>${name...}</c> interpolation in a definition.</summary>
    private static HashSet<string> ReferencedNames(object? definition)
    {
        var found = new HashSet<string>();
        void Walk(object? v)
        {
            switch (v)
            {
                case string s:
                    foreach (System.Text.RegularExpressions.Match m in InterpolationRef.Matches(s))
                        found.Add(m.Groups[1].Value);
                    break;
                case IDictionary<string, object?> d:
                    foreach (var x in d.Values) Walk(x);
                    break;
                case System.Collections.IEnumerable e when v is not string:
                    foreach (var x in e) Walk(x);
                    break;
            }
        }
        Walk(definition);
        return found;
    }

    public string ToYaml(string name, string description) => ToYaml(name, description, Variables, Resources, Outputs);

    private string ToYaml(string name, string description,
        Dictionary<string, object?> variables, Dictionary<string, object?> resources, Dictionary<string, object?> outputs)
    {
        var sb = new StringBuilder();
        sb.Append("name: ").AppendLine(Scalar(name));
        sb.Append("description: ").AppendLine(Scalar(description));
        sb.AppendLine("runtime: yaml");
        foreach (var (section, values) in new[]
        {
            ("config", Configuration), ("variables", variables),
            ("resources", resources), ("outputs", outputs),
        })
        {
            sb.Append(section).Append(':');
            if (values.Count == 0) { sb.AppendLine(" {}"); continue; }
            sb.AppendLine();
            WriteMap(sb, values, 1);
        }
        return sb.ToString();
    }

    private static string Scalar(object? v) => v switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        int or long or double => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)!,
        _ => JsonSerializer.Serialize(v.ToString()),
    };

    private static void WriteMap(StringBuilder sb, IDictionary<string, object?> map, int level)
    {
        var pad = new string(' ', level * 2);
        foreach (var (key, value) in map)
        {
            sb.Append(pad).Append(key).Append(':');
            WriteValue(sb, value, level);
        }
    }

    private static void WriteValue(StringBuilder sb, object? value, int level)
    {
        switch (value)
        {
            case IDictionary<string, object?> { Count: 0 }:
                sb.AppendLine(" {}");
                break;
            case IDictionary<string, object?> map:
                sb.AppendLine();
                WriteMap(sb, map, level + 1);
                break;
            case IList<object?> list:
                sb.AppendLine();
                foreach (var item in list)
                {
                    sb.Append(new string(' ', (level + 1) * 2)).Append('-');
                    WriteValue(sb, item, level + 1);
                }
                break;
            default:
                sb.Append(' ').AppendLine(Scalar(value));
                break;
        }
    }
}
