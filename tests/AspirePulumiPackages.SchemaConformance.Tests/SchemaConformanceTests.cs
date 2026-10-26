using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace AspirePulumiPackages.SchemaConformance.Tests;

/// <summary>
/// Schema-conformance tests for the hand-written Pulumi overlays (issue #65, docs/decisions/0001-overlay-generator.md).
/// Two layers:
/// 1. every azure-native token literal in src/**/*.cs (all integrations, including ones the demo AppHost does not use)
///    must exist in the schema;
/// 2. every Pulumi.yaml that <c>aspire publish</c> generates for the AppHost has its resource tokens, property paths
///    and <c>${res.prop}</c> output references checked against the schema.
/// Set PULUMI_PUBLISH_DIR to a directory of already published output to skip running <c>aspire publish</c>, and
/// AZURE_NATIVE_SCHEMA_PATH to a saved <c>pulumi package get-schema azure-native</c> file to skip generating it.
/// </summary>
public sealed partial class SchemaConformanceTests(PublishedProgramsFixture programs) : IClassFixture<PublishedProgramsFixture>
{
    private static readonly Lazy<AzureNativeSchema> Schema = new(() => AzureNativeSchema.Load(SourceTokens().Concat(PublishedProgramsFixture.AllTokens())));

    [Fact]
    public void Every_token_literal_in_source_exists_in_the_schema()
    {
        var schema = Schema.Value;
        var missing = SourceTokens()
            .Where(t => !schema.HasResource(t) && !schema.HasFunction(t))
            .Order().ToList();
        Assert.True(missing.Count == 0, "Unknown azure-native tokens in source:\n" + string.Join('\n', missing));
    }

    [Fact]
    public void Published_programs_conform_to_the_schema()
    {
        Assert.NotEmpty(programs.Programs);
        var validator = new PulumiYamlValidator(Schema.Value);
        var errors = programs.Programs.SelectMany(p => validator.Validate(p.Yaml, p.Name)).ToList();
        Assert.True(errors.Count == 0, "Schema mismatches:\n" + string.Join('\n', errors));
    }

    private static IEnumerable<string> SourceTokens()
    {
        var src = Path.Combine(RepoPaths.Root, "src");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => TokenLiteral().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Distinct();
    }

    // "azure-native:module:Name" or ":module/vYYYY:Name" inside a string literal (interpolated holes are not matched).
    [GeneratedRegex("\"(azure-native:[A-Za-z0-9]+:[A-Za-z0-9]+)\"")]
    private static partial Regex TokenLiteral();
}

/// <summary>Runs <c>aspire publish</c> for the real AppHost once and exposes the generated Pulumi.yaml programs.</summary>
public sealed class PublishedProgramsFixture : IDisposable
{
    private static readonly Lazy<List<(string Name, string Yaml)>> Published = new(Publish);

    public IReadOnlyList<(string Name, string Yaml)> Programs => Published.Value;

    public static IEnumerable<string> AllTokens() => Published.Value.SelectMany(p => PulumiYamlValidator.TokensIn(p.Yaml));

    public void Dispose() { }

    private static List<(string Name, string Yaml)> Publish()
    {
        var dir = Environment.GetEnvironmentVariable("PULUMI_PUBLISH_DIR");
        if (string.IsNullOrEmpty(dir))
        {
            dir = Path.Combine(Path.GetTempPath(), "aspire-pulumi-publish-" + Guid.NewGuid().ToString("N"));
            var appHost = Path.Combine(RepoPaths.Root, "src", "AspirePulumiPackages.AppHost");
            var psi = new ProcessStartInfo("aspire", $"publish -o \"{dir}\"")
            {
                WorkingDirectory = appHost,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.Environment["Parameters__api_key"] = "x";
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start 'aspire'.");
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            p.WaitForExit();
            if (p.ExitCode != 0) throw new InvalidOperationException($"'aspire publish' failed:\n{stdout.Result}\n{stderr.Result}");
        }
        return Directory.EnumerateFiles(dir, "Pulumi.yaml", SearchOption.AllDirectories)
            .Select(f => (Path.GetRelativePath(dir, f), File.ReadAllText(f)))
            .ToList();
    }
}
