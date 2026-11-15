namespace Sheridan.Aspire.Pulumi.Azure.ApiManagement;

/// <summary>API Management pricing tiers (mirrors the official <c>AzureApiManagementSku</c>).</summary>
public enum AzureApiManagementSku
{
    Consumption,
    Developer,
    Basic,
    BasicV2,
    Standard,
    StandardV2,
    Premium,
    PremiumV2,
}

/// <summary>Options for a new API Management service (mirrors the official <c>AzureApiManagementOptions</c>).</summary>
public sealed class AzureApiManagementOptions
{
    private int? _capacity;
    private AzureApiManagementSku? _sku;

    public required string PublisherEmail { get; init; }
    public required string PublisherName { get; init; }

    /// <summary>The tier; Developer when not set (as in the official integration).</summary>
    public AzureApiManagementSku Sku { get => _sku ?? AzureApiManagementSku.Developer; init => _sku = value; }

    /// <summary>Scale units; 0 for Consumption and 1 otherwise when not set.</summary>
    public int Capacity { get => _capacity ?? (Sku == AzureApiManagementSku.Consumption ? 0 : 1); init => _capacity = value; }

    /// <summary>
    /// Whether the service gets a system-assigned identity and a <c>principalId</c> output (default true; the official integration always adds one).
    /// Set to false for emulators that do not return an identity: <c>${apim.identity.principalId}</c> cannot be made null-safe in
    /// Pulumi YAML, so the output would fail the update when the identity is missing.
    /// </summary>
    public bool SystemAssignedIdentity { get; init; } = true;
}
