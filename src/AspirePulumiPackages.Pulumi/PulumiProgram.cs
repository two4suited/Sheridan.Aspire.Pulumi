using System.Text;
using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Pipelines;

namespace AspirePulumiPackages.Pulumi;

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

    /// <summary>Resources that must be created before container images can be pushed (e.g. the registry).</summary>
    public List<(string Type, string Name)> InfraTargets { get; } = [];

    public bool HasApps { get; set; }

    public void AddInfraResource(string name, string type, Dictionary<string, object?> definition)
    {
        Resources[name] = definition;
        InfraTargets.Add((type, name));
    }

    public string ToYaml(string name, string description)
    {
        var sb = new StringBuilder();
        sb.Append("name: ").AppendLine(Scalar(name));
        sb.Append("description: ").AppendLine(Scalar(description));
        sb.AppendLine("runtime: yaml");
        foreach (var (section, values) in new[]
        {
            ("config", Configuration), ("variables", Variables),
            ("resources", Resources), ("outputs", Outputs),
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
