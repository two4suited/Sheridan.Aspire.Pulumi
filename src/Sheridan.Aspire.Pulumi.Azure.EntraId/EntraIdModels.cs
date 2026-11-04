using System.Security.Cryptography;
using System.Text;

namespace Sheridan.Aspire.Pulumi.Azure.EntraId;

/// <summary>How a permission is granted: on behalf of a signed-in user (<see cref="Delegated"/>) or to the app itself (<see cref="Application"/>).</summary>
public enum EntraIdApiPermissionType
{
    /// <summary>A delegated permission (OAuth2 scope).</summary>
    Delegated,
    /// <summary>An application permission (app role).</summary>
    Application,
}

/// <summary>A permission on an API, by name (for example <c>User.Read</c>).</summary>
public sealed record EntraIdApiPermission(string Name, EntraIdApiPermissionType Type)
{
    public static EntraIdApiPermission Delegated(string name) => new(Validate(name), EntraIdApiPermissionType.Delegated);
    public static EntraIdApiPermission Application(string name) => new(Validate(name), EntraIdApiPermissionType.Application);

    private static string Validate(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }
}

/// <summary>An API that is not modeled in the AppHost, identified by its application (client) ID.</summary>
public sealed record EntraIdApi
{
    private EntraIdApi(string applicationId, string name) { ApplicationId = applicationId; Name = name; }

    public string ApplicationId { get; }
    internal string Name { get; }

    public static EntraIdApi MicrosoftGraph { get; } = new("00000003-0000-0000-c000-000000000000", "MicrosoftGraph");
    public static EntraIdApi AzureResourceManager { get; } = new("797f4846-ba00-4fd7-ba43-dac1f8f63013", "AzureResourceManager");
    public static EntraIdApi AzureStorage { get; } = new("e406a681-f3d4-42a8-90b6-c2b029497af1", "AzureStorage");

    /// <summary>Any API by its application (client) ID.</summary>
    public static EntraIdApi FromApplicationId(string applicationId)
    {
        if (!Guid.TryParse(applicationId, out var g)) throw new ArgumentException("The application ID must be a GUID.", nameof(applicationId));
        return new(g.ToString(), "Api" + g.ToString("N")[..8]);
    }
}

/// <summary>A delegated permission scope exposed by an API registration. Mirrors the official <c>EntraIdScope</c>.</summary>
public sealed class EntraIdScope(string name, string description)
{
    public string Name { get; } = Required(name, nameof(name));
    public string Description { get; set; } = Required(description, nameof(description));

    /// <summary>Explicit scope ID; when null a stable GUID is derived from the registration name and scope name.</summary>
    public Guid? Id { get; set; }

    public string? AdminConsentDisplayName { get; set; }
    public string? UserConsentDisplayName { get; set; }
    public string? UserConsentDescription { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>When true, only an administrator can consent (<c>Admin</c>); otherwise users can (<c>User</c>).</summary>
    public bool RequiresAdminConsent { get; set; }

    internal static string Required(string v, string n) { ArgumentException.ThrowIfNullOrWhiteSpace(v, n); return v; }
}

/// <summary>An application role exposed by a registration. Mirrors the official <c>EntraIdAppRole</c>.</summary>
public sealed class EntraIdAppRole(string value, string displayName, string description, IReadOnlyList<string> allowedMemberTypes)
{
    public string Value { get; } = EntraIdScope.Required(value, nameof(value));
    public string DisplayName { get; } = EntraIdScope.Required(displayName, nameof(displayName));
    public string Description { get; } = EntraIdScope.Required(description, nameof(description));

    /// <summary><c>Application</c> and/or <c>User</c>.</summary>
    public IReadOnlyList<string> AllowedMemberTypes { get; } = Validate(allowedMemberTypes);

    /// <summary>Explicit role ID; when null a stable GUID is derived from the registration name and role value.</summary>
    public Guid? Id { get; set; }

    /// <summary>Set to false to retire a role: Graph rejects deleting an enabled role, so disable it first, deploy, then remove it.</summary>
    public bool IsEnabled { get; set; } = true;

    private static IReadOnlyList<string> Validate(IReadOnlyList<string> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        if (types.Count == 0) throw new ArgumentException("At least one allowed member type is required.", nameof(types));
        foreach (var t in types)
            if (t is not ("Application" or "User")) throw new ArgumentException($"Allowed member type '{t}' must be 'Application' or 'User'.", nameof(types));
        return types;
    }
}

internal static class EntraIdIds
{
    /// <summary>Deterministic GUID (name-based, SHA-256) so scope/role IDs never change between deployments.</summary>
    public static Guid Stable(string kind, string resourceName, string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{kind}:{resourceName}:{key}"));
        var b = hash[..16];
        b[6] = (byte)((b[6] & 0x0F) | 0x50);
        b[8] = (byte)((b[8] & 0x3F) | 0x80);
        return new Guid(b);
    }
}
