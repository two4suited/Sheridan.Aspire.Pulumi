using System.Security.Cryptography;
using System.Text;

namespace Sheridan.Aspire.Pulumi;

/// <summary>
/// Display names for Microsoft Entra ID objects (app registrations). Entra display names are not globally unique and accept
/// any characters (1-256), so the names are readable, built from the app name: <c>{project}-{resource}-{stack}</c>
/// (the stack keeps registrations of different environments apart in the tenant). Deterministic; when longer than 256
/// characters the longest segments are trimmed and a 6-character hash of the full name is appended.
/// </summary>
public static class PulumiEntraNaming
{
    public const int MaxDisplayNameLength = 256;
    private const int HashLength = 6;

    public static string DisplayName(string? projectName, string resourceName, string? stackName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        var segments = new[] { projectName, resourceName, stackName }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).ToList();
        var full = string.Join("-", segments);
        if (full.Length <= MaxDisplayNameLength) return full;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full))).ToLowerInvariant()[..HashLength];
        var budget = MaxDisplayNameLength - HashLength - 1;
        while (string.Join("-", segments).Length > budget)
        {
            var i = segments.IndexOf(segments.OrderByDescending(s => s.Length).First());
            segments[i] = segments[i][..^1];
        }
        return string.Join("-", segments.Where(s => s.Length > 0)).TrimEnd('-') + "-" + hash;
    }

    /// <summary>Validates an explicit display name (1-256 characters, not blank); returns it.</summary>
    public static string ValidateDisplayName(string? displayName, string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > MaxDisplayNameLength)
            throw new ArgumentException(
                $"'{displayName}' is not a valid Entra ID display name: it must be 1-{MaxDisplayNameLength} characters and not blank (length {displayName?.Length ?? 0}).",
                paramName ?? "displayName");
        return displayName;
    }
}
