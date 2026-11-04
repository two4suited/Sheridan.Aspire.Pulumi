using Sheridan.Aspire.Pulumi;
using Sheridan.Aspire.Pulumi.Azure.Base;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Naming.Tests;

/// <summary>Names applied to Cognitive Services / Foundry / Connector Namespace / Entra ID.</summary>
public class AiAndMiscNamingTests
{
    private const string App = "aspire-pulumi-packages";

    [Fact]
    public void OpenAIAccountAndSubdomainAreComposedFromAppResourceAndStack()
    {
        Assert.Equal("aspire-pulumi-packages-openai-azure-test", AzureNaming.Compose(AzureNameKind.CognitiveServicesAccount, App, "openai", "azure-test"));
        Assert.Equal("aspire-pulumi-packages-openai-azure-test", AzureNaming.Compose(AzureNameKind.CognitiveServicesSubdomain, App, "openai", "azure-test"));
    }

    [Fact]
    public void FoundryProjectAppInsightsNameIncludesProjectAndSuffix() =>
        Assert.Equal("aspire-pulumi-packages-foundry-agents-appi-azure-test",
            AzureNaming.Compose(AzureNameKind.ApplicationInsights, App, "foundry", "azure-test", "agents-appi"));

    [Fact]
    public void LongSubdomainIsShortenedWithinSixtyFourAndStable()
    {
        var a = AzureNaming.Compose(AzureNameKind.CognitiveServicesSubdomain, App, "a-very-long-foundry-resource-name-for-the-assistant", "production-west-europe");
        var b = AzureNaming.Compose(AzureNameKind.CognitiveServicesSubdomain, App, "a-very-long-foundry-resource-name-for-the-assistant", "production-west-europe");
        Assert.Equal(a, b);
        Assert.True(a.Length <= 64);
        Assert.Empty(AzureNaming.Validate(AzureNameKind.CognitiveServicesSubdomain, a));
    }

    [Fact]
    public void ConnectorGatewayNameIsLowercaseAlphanumericAndFits()
    {
        var name = AzureNaming.Compose(AzureNameKind.ConnectorGateway, App, "connectors", "azure-test");
        Assert.Matches("^[a-z][a-z0-9]{2,23}$", name);
        Assert.Equal(name, AzureNaming.Compose(AzureNameKind.ConnectorGateway, App, "connectors", "azure-test"));
        Assert.NotEqual(name, AzureNaming.Compose(AzureNameKind.ConnectorGateway, App, "connectors", "azure-prod"));
    }

    [Fact]
    public void ConnectorGatewayOverrideIsValidated() =>
        Assert.Throws<ArgumentException>(() => AzureNaming.ValidateOverride(AzureNameKind.ConnectorGateway, "has-hyphen"));

    [Fact]
    public void InvalidSubdomainOverrideThrows()
    {
        var ex = Assert.Throws<ArgumentException>(() => AzureNaming.ValidateOverride(AzureNameKind.CognitiveServicesSubdomain, "bad_name"));
        Assert.Contains("Microsoft.CognitiveServices", ex.Message);
    }

    [Fact]
    public void EntraDisplayNameIsAppResourceStack() =>
        Assert.Equal("aspire-pulumi-packages-app-registration-entra-test", PulumiEntraNaming.DisplayName(App, "app-registration", "entra-test"));

    [Fact]
    public void EntraDisplayNameOverLimitIsTrimmedWithStableHash()
    {
        var name = PulumiEntraNaming.DisplayName(new string('p', 200), new string('r', 200), "dev");
        Assert.Equal(PulumiEntraNaming.MaxDisplayNameLength, name.Length);
        Assert.Equal(name, PulumiEntraNaming.DisplayName(new string('p', 200), new string('r', 200), "dev"));
        Assert.NotEqual(name, PulumiEntraNaming.DisplayName(new string('p', 200), new string('r', 201), "dev"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankEntraDisplayNameOverrideThrows(string value) =>
        Assert.Throws<ArgumentException>(() => PulumiEntraNaming.ValidateDisplayName(value));
}
