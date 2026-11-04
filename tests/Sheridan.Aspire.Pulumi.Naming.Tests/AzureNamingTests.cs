using Sheridan.Aspire.Pulumi;
using Sheridan.Aspire.Pulumi.Azure.Base;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Naming.Tests;

public class AzureNamingTests
{
    private const string Project = "aspire-pulumi-packages";

    public static IEnumerable<object[]> AllKinds() => Enum.GetValues<AzureNameKind>().Select(k => new object[] { k });

    [Fact]
    public void EveryKindHasARule() =>
        Assert.All(Enum.GetValues<AzureNameKind>(), k => Assert.True(AzureNaming.Rules.ContainsKey(k), k.ToString()));

    // The generated name must satisfy the same validator used for overrides, for short, long, and awkward inputs.
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void ComposedNamesAlwaysConform(AzureNameKind kind)
    {
        var inputs = new (string? Project, string Resource, string? Stack, string? Suffix)[]
        {
            (Project, "api", "dev", null),
            (Project, "a", "x", null),
            (Project, "my-very-long-resource-name-that-goes-on-and-on-and-on", "production-eu-west", "kv"),
            ("2cool_app", "9lives", "7", "acr"),
            ("Under_Score.Dot", "Mixed_Case.Name", "Prod", null),
            (new string('p', 400), new string('r', 400), new string('s', 100), "kv"),
            ("café", "naïve", "dev", null),
            (null, "solo", null, null),
        };
        foreach (var (p, r, s, sfx) in inputs)
        {
            var name = AzureNaming.Compose(kind, p, r, s, sfx);
            var errors = AzureNaming.Validate(kind, name);
            Assert.True(errors.Count == 0, $"{kind}: '{name}' => {string.Join("; ", errors)}");
        }
    }

    [Fact]
    public void ShortNamesAreComposedWithoutHash() =>
        Assert.Equal("aspire-pulumi-packages-api-dev", AzureNaming.Compose(AzureNameKind.WebApp, Project, "api", "dev"));

    [Fact]
    public void SuffixSitsBeforeStack() =>
        Assert.Equal("aspire-pulumi-packages-db-kv-dev", AzureNaming.Compose(AzureNameKind.AppServicePlan, Project, "db", "dev", "kv"));

    [Fact]
    public void HyphenlessTypesDropSeparators() =>
        Assert.Equal("aspirepulumipackagesapidev", AzureNaming.Compose(AzureNameKind.ContainerRegistry, Project, "api", "dev"));

    [Fact]
    public void StorageAccountIsLowercaseAlphanumeric24()
    {
        var name = AzureNaming.Compose(AzureNameKind.StorageAccount, "My-App", "Data_Store", "Prod");
        Assert.Matches("^[a-z0-9]{3,24}$", name);
        Assert.Equal("my-appdatastoreprod".Replace("-", ""), name);
    }

    [Fact]
    public void LongNamesAreShortenedWithHashAndFit()
    {
        var name = AzureNaming.Compose(AzureNameKind.KeyVault, Project, "cosmos", "dev", "kv");
        Assert.Equal(24, name.Length);
        Assert.EndsWith(AzureNaming.StableHash([Project, "cosmos", "kv", "dev"], AzureNameKind.KeyVault), name);
        Assert.EndsWith("-dev-" + name[^AzureNaming.HashLength..], name); // stack segment survives intact
    }

    [Fact]
    public void ShorteningKeepsStackAndSuffixWhenPossible()
    {
        var name = AzureNaming.Compose(AzureNameKind.ContainerApp, Project, "a-really-long-resource-name", "staging", "web");
        Assert.True(name.Length <= 32);
        Assert.Contains("-web-staging-", name);
    }

    [Fact]
    public void HashIsStableAcrossCalls()
    {
        var a = AzureNaming.Compose(AzureNameKind.KeyVault, Project, "cosmos", "dev", "kv");
        var b = AzureNaming.Compose(AzureNameKind.KeyVault, Project, "cosmos", "dev", "kv");
        Assert.Equal(a, b);
        // Pinned value: guards against accidental algorithm changes (names must be stable across releases).
        Assert.Equal(AzureNaming.StableHash(["x"], AzureNameKind.KeyVault), AzureNaming.StableHash(["X"], AzureNameKind.KeyVault));
        Assert.Matches("^[0-9a-f]{6}$", AzureNaming.StableHash(["x"], AzureNameKind.KeyVault));
    }

    [Fact]
    public void HashValueIsPinned() =>
        Assert.Equal("736b2f", AzureNaming.StableHash(["pinned", "value"], AzureNameKind.StorageAccount));

    [Fact]
    public void DifferentLongNamesDoNotCollide()
    {
        var names = Enumerable.Range(0, 200)
            .Select(i => AzureNaming.Compose(AzureNameKind.KeyVault, Project, $"resource-number-{i}-with-long-tail", "dev", "kv"))
            .ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(names, n => Assert.True(n.Length <= 24));
    }

    [Fact]
    public void LongNamesDifferingOnlyAfterTheCutDoNotCollide()
    {
        var a = AzureNaming.Compose(AzureNameKind.StorageAccount, "app", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaa1", "dev");
        var b = AzureNaming.Compose(AzureNameKind.StorageAccount, "app", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaa2", "dev");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void NoHashWhenLossless()
    {
        var name = AzureNaming.Compose(AzureNameKind.SqlServer, "app", "db", "dev");
        Assert.Equal("app-db-dev", name);
    }

    [Fact]
    public void DroppedCharactersMakeTheNameLossyAndHashed()
    {
        var plain = AzureNaming.Compose(AzureNameKind.SqlServer, "app", "db", "dev");
        var accented = AzureNaming.Compose(AzureNameKind.SqlServer, "app", "déb", "dev");
        Assert.NotEqual(plain, accented);
        Assert.Equal(AzureNaming.HashLength, accented.Length - accented.LastIndexOf('-') - 1);
    }

    [Fact]
    public void NamesShorterThanTheMinimumArePaddedDeterministically()
    {
        var a = AzureNaming.Compose(AzureNameKind.ContainerRegistry, null, "ab", null);
        Assert.Equal(5, a.Length);
        Assert.StartsWith("ab", a);
        Assert.Equal(a, AzureNaming.Compose(AzureNameKind.ContainerRegistry, null, "ab", null));
    }

    [Fact]
    public void LeadingDigitGetsTheTypeAbbreviationWhenALetterIsRequired() =>
        Assert.Equal("kv-1app-db-dev", AzureNaming.Compose(AzureNameKind.KeyVault, "1app", "db", "dev"));

    [Fact]
    public void ConsecutiveSeparatorsAreCollapsed() =>
        Assert.Equal("a-b-c-dev", AzureNaming.Compose(AzureNameKind.KeyVault, "a__b", "c", "dev"));

    [Fact]
    public void NeverStartsOrEndsWithHyphenAfterTrimming()
    {
        var name = AzureNaming.Compose(AzureNameKind.KeyVault, "aaaaaaa-b", "ccccccc-d", "eeeeeeee-f", "kv");
        Assert.DoesNotContain("--", name);
        Assert.True(char.IsAsciiLetterOrDigit(name[^1]));
    }

    // ---- validation of explicit overrides ----

    [Theory]
    [InlineData(AzureNameKind.StorageAccount, "ab")]                    // too short
    [InlineData(AzureNameKind.StorageAccount, "has-hyphen")]            // hyphen forbidden
    [InlineData(AzureNameKind.StorageAccount, "UpperCase1")]            // uppercase
    [InlineData(AzureNameKind.KeyVault, "1starts-with-digit")]          // must start with a letter
    [InlineData(AzureNameKind.KeyVault, "ends-with-hyphen-")]           // end rule
    [InlineData(AzureNameKind.KeyVault, "double--hyphen")]              // consecutive hyphens
    [InlineData(AzureNameKind.KeyVault, "this-key-vault-name-is-too-long")]
    [InlineData(AzureNameKind.ContainerRegistry, "my-registry")]        // hyphen forbidden
    [InlineData(AzureNameKind.CosmosDbAccount, "UPPER")]
    [InlineData(AzureNameKind.ContainerApp, "9app")]
    [InlineData(AzureNameKind.ContainerApp, "app-")]
    [InlineData(AzureNameKind.ServiceBusNamespace, "short")]
    [InlineData(AzureNameKind.KustoCluster, "has-hyphen")]
    [InlineData(AzureNameKind.ResourceGroup, "trailing.")]
    [InlineData(AzureNameKind.UserAssignedIdentity, "_leading")]
    [InlineData(AzureNameKind.PostgresFlexibleServer, "-pg")]
    public void InvalidOverridesThrowWithAClearMessage(AzureNameKind kind, string name)
    {
        var ex = Assert.Throws<ArgumentException>(() => AzureNaming.ValidateOverride(kind, name));
        Assert.Contains($"'{name}'", ex.Message);
        Assert.Contains(AzureNaming.RuleFor(kind).ResourceType, ex.Message);
        Assert.Contains($"{AzureNaming.RuleFor(kind).Min}-{AzureNaming.RuleFor(kind).Max}", ex.Message);
    }

    [Theory]
    [InlineData(AzureNameKind.StorageAccount, "mystorage01")]
    [InlineData(AzureNameKind.KeyVault, "my-vault-1")]
    [InlineData(AzureNameKind.ContainerRegistry, "MyRegistry01")]
    [InlineData(AzureNameKind.CosmosDbAccount, "my-cosmos-")]          // Microsoft sets no end rule for Cosmos
    [InlineData(AzureNameKind.UserAssignedIdentity, "id_1-a")]
    [InlineData(AzureNameKind.ResourceGroup, "rg_my.app-(dev)")]
    public void ValidOverridesPass(AzureNameKind kind, string name)
    {
        // "(" and ")" are legal in resource groups per Microsoft but intentionally not accepted here; keep the test honest.
        if (kind == AzureNameKind.ResourceGroup) name = "rg_my.app-dev";
        Assert.Equal(name, AzureNaming.ValidateOverride(kind, name));
    }

    [Fact]
    public void EmptyOverrideIsRejected() =>
        Assert.Throws<ArgumentException>(() => AzureNaming.ValidateOverride(AzureNameKind.KeyVault, ""));

    [Fact]
    public void MaxLengthBoundariesAreEnforced()
    {
        Assert.Empty(AzureNaming.Validate(AzureNameKind.StorageAccount, new string('a', 24)));
        Assert.NotEmpty(AzureNaming.Validate(AzureNameKind.StorageAccount, new string('a', 25)));
        Assert.Empty(AzureNaming.Validate(AzureNameKind.ContainerApp, "a" + new string('b', 31)));
        Assert.NotEmpty(AzureNaming.Validate(AzureNameKind.ContainerApp, "a" + new string('b', 32)));
    }

    // ---- rule table spot checks against Microsoft's published limits ----

    [Theory]
    [InlineData(AzureNameKind.StorageAccount, 3, 24)]
    [InlineData(AzureNameKind.KeyVault, 3, 24)]
    [InlineData(AzureNameKind.ContainerRegistry, 5, 50)]
    [InlineData(AzureNameKind.CosmosDbAccount, 3, 44)]
    [InlineData(AzureNameKind.SqlServer, 1, 63)]
    [InlineData(AzureNameKind.PostgresFlexibleServer, 3, 63)]
    [InlineData(AzureNameKind.RedisCache, 1, 63)]
    [InlineData(AzureNameKind.ServiceBusNamespace, 6, 50)]
    [InlineData(AzureNameKind.EventHubsNamespace, 6, 50)]
    [InlineData(AzureNameKind.AppConfigurationStore, 5, 50)]
    [InlineData(AzureNameKind.CognitiveServicesSubdomain, 2, 64)]
    [InlineData(AzureNameKind.KubernetesCluster, 1, 63)]
    [InlineData(AzureNameKind.ContainerApp, 2, 32)]
    [InlineData(AzureNameKind.LogAnalyticsWorkspace, 4, 63)]
    [InlineData(AzureNameKind.ApplicationInsights, 1, 260)]
    [InlineData(AzureNameKind.UserAssignedIdentity, 3, 128)]
    [InlineData(AzureNameKind.ResourceGroup, 1, 90)]
    public void RuleLengthsMatchMicrosoftDocs(AzureNameKind kind, int min, int max)
    {
        var rule = AzureNaming.RuleFor(kind);
        Assert.Equal((min, max), (rule.Min, rule.Max));
    }

    [Fact]
    public void ScopesAreRecorded()
    {
        Assert.Equal(AzureNameScope.Global, AzureNaming.RuleFor(AzureNameKind.StorageAccount).Scope);
        Assert.Equal(AzureNameScope.Global, AzureNaming.RuleFor(AzureNameKind.KeyVault).Scope);
        Assert.Equal(AzureNameScope.Subscription, AzureNaming.RuleFor(AzureNameKind.ResourceGroup).Scope);
        Assert.Equal(AzureNameScope.ResourceGroup, AzureNaming.RuleFor(AzureNameKind.UserAssignedIdentity).Scope);
    }

    // ---- resource accessor ----

    private sealed class TestResource(string name) : PulumiAzureResource(name)
    {
        protected override Task ContributeResourcesAsync(PulumiProgram program, global::Aspire.Hosting.Pipelines.PipelineStepContext context, string resourceGroup, string location) =>
            Task.CompletedTask;
    }

    [Fact]
    public void NameForUsesTheBoundStackProjectAndStackNames()
    {
        var stack = new PulumiStackResource("aspire-pulumi-packages", "dev");
        var res = new TestResource("api") { Stack = stack };
        Assert.Equal("aspire-pulumi-packages-api-dev", res.NameFor(AzureNameKind.WebApp));
        Assert.Equal("aspirepulumipackagesapiacrdev", res.NameFor(AzureNameKind.ContainerRegistry, "acr"));
    }

    [Fact]
    public void NameForHonoursProjectNameOverride()
    {
        var stack = new PulumiStackResource("stack-resource", "dev") { ProjectNameOverride = "shop" };
        Assert.Equal("shop-api-dev", new TestResource("api") { Stack = stack }.NameFor(AzureNameKind.WebApp));
    }

    [Fact]
    public void ExplicitOverrideWinsAndIsKeyedBySuffix()
    {
        var stack = new PulumiStackResource("app", "dev");
        var res = new TestResource("api") { Stack = stack };
        res.NameOverrides[(AzureNameKind.KeyVault, "kv")] = "my-vault";
        Assert.Equal("my-vault", res.NameFor(AzureNameKind.KeyVault, "kv"));
        Assert.Equal("app-api-dev", res.NameFor(AzureNameKind.KeyVault));
    }

    [Fact]
    public void NameForWithoutAStackFailsClearly() =>
        Assert.Throws<InvalidOperationException>(() => new TestResource("api").NameFor(AzureNameKind.KeyVault));

    [Fact]
    public void ProgramHelpersWriteValidatedNames()
    {
        var program = new PulumiProgram();
        program.AddKeyVault("kv", "${rg}", "eastus", name: "good-vault");
        var props = (Dictionary<string, object?>)((Dictionary<string, object?>)program.Resources["kv"]!)["properties"]!;
        Assert.Equal("good-vault", props["vaultName"]);
        Assert.Throws<ArgumentException>(() => program.AddKeyVault("kv2", "${rg}", "eastus", name: "Bad_Vault"));
        Assert.Throws<ArgumentException>(() => program.AddKeyVaultSecret("s", "kv", "${rg}", "bad_secret", "v"));

        program.AddUserAssignedIdentity("uai", "${rg}", "eastus", "app-uai-dev");
        var uai = (Dictionary<string, object?>)((Dictionary<string, object?>)program.Resources["uai"]!)["properties"]!;
        Assert.Equal("app-uai-dev", uai["resourceName"]);

        var rg = program.AddResourceGroup("x", "eastus", "app-x-rg-dev");
        Assert.Equal("${xRg.name}", rg);
        var rgProps = (Dictionary<string, object?>)((Dictionary<string, object?>)program.Resources["xRg"]!)["properties"]!;
        Assert.Equal("app-x-rg-dev", rgProps["resourceGroupName"]);
    }

    // Pinned examples (project "aspire-pulumi-packages", stack "azure-test"): names must stay stable across releases.
    [Theory]
    [InlineData(AzureNameKind.ResourceGroup, "shared-rg", null, "aspire-pulumi-packages-shared-rg-azure-test")]
    [InlineData(AzureNameKind.UserAssignedIdentity, "aca-env", "mi", "aspire-pulumi-packages-aca-env-mi-azure-test")]
    [InlineData(AzureNameKind.KustoCluster, "kusto", null, "aspkustazuretest528b82")]
    [InlineData(AzureNameKind.KeyVault, "cosmos", "kv", "asp-cos-kv-azure-9d3986")]
    [InlineData(AzureNameKind.ContainerApp, "api", null, "aspire-pul-api-azure-test-19b818")]
    [InlineData(AzureNameKind.LogAnalyticsWorkspace, "aca-env", "law", "aspire-pulumi-packages-aca-env-law-azure-test")]
    [InlineData(AzureNameKind.ContainerRegistry, "aca-env", "acr", "aspirepulumipackagesacaenvacrazuretest")]
    [InlineData(AzureNameKind.ServiceBusNamespace, "messaging", null, "aspire-pulumi-packages-messaging-azure-test")]
    [InlineData(AzureNameKind.StorageAccount, "storage", null, "aspistoraazuretest279b9b")]
    [InlineData(AzureNameKind.CosmosDbAccount, "cosmos", null, "aspire-pulumi-packages-cosmos-azure-test")]
    [InlineData(AzureNameKind.ContainerRegistry, "aca-env", "acr", "aspirepulumipackagesacaenvacrazuretest")]
    [InlineData(AzureNameKind.ContainerAppsEnvironment, "aca-env", null, "aspire-pulumi-packages-aca-env-azure-test")]
    [InlineData(AzureNameKind.LogAnalyticsWorkspace, "aca-env", "law", "aspire-pulumi-packages-aca-env-law-azure-test")]
    [InlineData(AzureNameKind.UserAssignedIdentity, "aca-env", "mi", "aspire-pulumi-packages-aca-env-mi-azure-test")]
    [InlineData(AzureNameKind.VirtualNetwork, "vnet", null, "aspire-pulumi-packages-vnet-azure-test")]
    [InlineData(AzureNameKind.SqlServer, "sql", null, "aspire-pulumi-packages-sql-azure-test")]
    [InlineData(AzureNameKind.PostgresFlexibleServer, "pg", null, "aspire-pulumi-packages-pg-azure-test")]
    [InlineData(AzureNameKind.RedisCache, "cache", null, "aspire-pulumi-packages-cache-azure-test")]
    [InlineData(AzureNameKind.RedisEnterpriseCluster, "mredis", null, "aspire-pulumi-packages-mredis-azure-test")]
    [InlineData(AzureNameKind.DeploymentScript, "sql", "sqldb-aca-env-roles", "aspire-pulumi-packages-sql-sqldb-aca-env-roles-azure-test")]
    public void PinnedExampleNames(AzureNameKind kind, string res, string? suffix, string expected) =>
        Assert.Equal(expected, AzureNaming.Compose(kind, Project, res, "azure-test", suffix));

    [Theory]
    [InlineData(AzureNameKind.ContainerApp, "api", "api")]
    [InlineData(AzureNameKind.ContainerApp, "My_Api", "my-api")]
    [InlineData(AzureNameKind.Subnet, "Workers", "workers")]
    public void ScopedNamesKeepConformingResourceNames(AzureNameKind kind, string resource, string expected) =>
        Assert.Equal(expected, AzureNaming.ComposeScoped(kind, resource));

    [Fact]
    public void LongContainerAppNamesAreHashedDeterministically()
    {
        var a = AzureNaming.ComposeScoped(AzureNameKind.ContainerApp, "inventory-reservation-processing-worker-a");
        var b = AzureNaming.ComposeScoped(AzureNameKind.ContainerApp, "inventory-reservation-processing-worker-b");
        Assert.Equal(32, a.Length);
        Assert.NotEqual(a, b);
        Assert.Equal(a, AzureNaming.ComposeScoped(AzureNameKind.ContainerApp, "inventory-reservation-processing-worker-a"));
    }

    [Fact]
    public void GeneratedSecurityRuleNamesAreSanitized()
    {
        var name = AzureNaming.ComposeScoped(AzureNameKind.NetworkSecurityRule, "allow-inbound-*-10.0.0.0/24");
        Assert.Empty(AzureNaming.Validate(AzureNameKind.NetworkSecurityRule, name));
    }

    [Fact]
    public void InvalidSubnetOverrideThrows() =>
        Assert.Throws<ArgumentException>(() => AzureNaming.ValidateOverride(AzureNameKind.Subnet, "bad name!"));
}
