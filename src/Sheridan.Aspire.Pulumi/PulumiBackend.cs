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
        pathOrUrl.Contains("://", StringComparison.Ordinal) ? pathOrUrl : "file://" + pathOrUrl;

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

    /// <summary>Renders <c>Pulumi.&lt;stack&gt;.yaml</c>, keeping an existing <c>encryptionsalt</c> (written by <c>pulumi stack init</c> for passphrase-encrypted stacks).</summary>
    public static string RenderStackFile(IReadOnlyList<string>? escImports, IReadOnlyDictionary<string, string> config, string? existingFile)
    {
        var sb = new System.Text.StringBuilder();
        if (escImports is { Count: > 0 })
        {
            sb.AppendLine("environment:");
            foreach (var e in escImports) sb.Append("  - ").AppendLine(e);
        }
        var salt = ExistingSalt(existingFile);
        if (salt is not null) sb.Append("encryptionsalt: ").AppendLine(salt);
        if (config.Count > 0)
        {
            sb.AppendLine("config:");
            foreach (var (k, v) in config)
                sb.Append("  ").Append(JsonSerializer.Serialize(k)).Append(": ").AppendLine(JsonSerializer.Serialize(v));
        }
        return sb.Length == 0 ? "{}\n" : sb.ToString();
    }

    private static string? ExistingSalt(string? existingFile)
    {
        if (existingFile is null) return null;
        foreach (var line in existingFile.Split('\n'))
            if (line.StartsWith("encryptionsalt:", StringComparison.Ordinal))
                return line["encryptionsalt:".Length..].Trim();
        return null;
    }
}
