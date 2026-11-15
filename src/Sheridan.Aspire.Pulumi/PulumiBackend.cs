using System.Text.Json;

namespace Sheridan.Aspire.Pulumi;

/// <summary>Detects whether the Pulumi backend in use is Pulumi Cloud or a self-managed (local/file/object-store) backend.</summary>
internal static class PulumiBackend
{
    /// <summary>True when <paramref name="url"/> is a self-managed backend (file://, s3://, azblob://, gs://, ...) rather than Pulumi Cloud / an http(s) service backend.</summary>
    public static bool IsLocalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Normalises a directory path or URL to a <c>file://</c> backend URL.</summary>
    public static string ToFileUrl(string pathOrUrl) =>
        pathOrUrl.Contains("://", StringComparison.Ordinal) ? pathOrUrl
        : pathOrUrl == "~" || pathOrUrl.StartsWith("~/", StringComparison.Ordinal) ? "file://" + pathOrUrl
        : "file://" + Path.GetFullPath(pathOrUrl).Replace('\\', '/');

    /// <summary>Creates the state directory of a <c>file://</c> backend (pulumi refuses to open a missing one). No-op for other schemes and <c>~</c>.</summary>
    public static void EnsureFileBackendDirectory(string? url)
    {
        const string prefix = "file://";
        if (url is null || !url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
        var path = url[prefix.Length..].Split('?')[0];
        if (path.Length == 0 || path == "~" || path.StartsWith("~/", StringComparison.Ordinal)) return;
        Directory.CreateDirectory(path);
    }

    /// <summary>The backend URL the CLI will use: explicit opt-in, else PULUMI_BACKEND_URL, else the current <c>pulumi login</c>.</summary>
    public static string? Resolve(string? explicitUrl)
    {
        if (!string.IsNullOrWhiteSpace(explicitUrl)) return explicitUrl;
        var env = Environment.GetEnvironmentVariable("PULUMI_BACKEND_URL");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        return CurrentLoginUrl();
    }

    private static string? CurrentLoginUrl()
    {
        try
        {
            var home = Environment.GetEnvironmentVariable("PULUMI_HOME");
            if (string.IsNullOrWhiteSpace(home))
                home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pulumi");
            var file = Path.Combine(home, "credentials.json");
            if (!File.Exists(file)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            return doc.RootElement.TryGetProperty("current", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Renders <c>Pulumi.&lt;stack&gt;.yaml</c>, keeping an existing <c>encryptionsalt</c>, <c>secretsprovider</c> and <c>encryptedkey</c> (written by <c>pulumi stack init</c>).</summary>
    public static string RenderStackFile(IReadOnlyList<string>? escImports, IReadOnlyDictionary<string, string> config, string? existingFile)
    {
        var sb = new System.Text.StringBuilder();
        if (escImports is { Count: > 0 })
        {
            sb.AppendLine("environment:");
            foreach (var e in escImports) sb.Append("  - ").AppendLine(e);
        }
        foreach (var line in PreservedLines(existingFile)) sb.AppendLine(line);
        if (config.Count > 0)
        {
            sb.AppendLine("config:");
            foreach (var (k, v) in config)
                sb.Append("  ").Append(JsonSerializer.Serialize(k)).Append(": ").AppendLine(JsonSerializer.Serialize(v));
        }
        return sb.Length == 0 ? "{}\n" : sb.ToString();
    }

    // Written by `pulumi stack init`/`config set --secret`; losing them would orphan existing ciphertext.
    private static readonly string[] PreservedKeys = ["secretsprovider:", "encryptedkey:", "encryptionsalt:"];

    private static IEnumerable<string> PreservedLines(string? existingFile)
    {
        if (existingFile is null) yield break;
        foreach (var raw in existingFile.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (PreservedKeys.Any(k => line.StartsWith(k, StringComparison.Ordinal))) yield return line;
        }
    }
}
