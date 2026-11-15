using Aspire.Hosting;
using Sheridan.Aspire.Pulumi;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

public sealed class LocalBackendTests
{
    [Theory]
    [InlineData("file://~", true)]
    [InlineData("file:///tmp/state", true)]
    [InlineData("s3://bucket/path", true)]
    [InlineData("azblob://container", true)]
    [InlineData("https://api.pulumi.com", false)]
    [InlineData("http://pulumi.example.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsLocalUrl_distinguishes_self_managed_backends(string? url, bool expected) =>
        Assert.Equal(expected, PulumiBackend.IsLocalUrl(url));

    [Fact]
    public void ToFileUrl_wraps_plain_paths_only()
    {
        Assert.Equal("file:///tmp/s", PulumiBackend.ToFileUrl("/tmp/s"));
        Assert.Equal("s3://b", PulumiBackend.ToFileUrl("s3://b"));
    }

    [Fact]
    public void EnsureFileBackendDirectory_creates_the_state_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pulumi-state-" + Guid.NewGuid().ToString("N"), "nested");
        try
        {
            PulumiBackend.EnsureFileBackendDirectory(PulumiBackend.ToFileUrl(dir));
            Assert.True(Directory.Exists(dir));
        }
        finally { Directory.Delete(Path.GetDirectoryName(dir)!, true); }
    }

    [Fact]
    public void ToFileUrl_makes_relative_paths_absolute()
    {
        var url = PulumiBackend.ToFileUrl("./state");
        Assert.StartsWith("file://", url);
        Assert.DoesNotContain("./", url);
    }

    [Fact]
    public void WithLocalBackend_passes_the_url_per_call_not_via_the_process_environment()
    {
        var builder = DistributedApplication.CreateBuilder();
        var stack = builder.AddPulumiStack("infra", "dev").WithLocalBackend("/tmp/s");
        Assert.Equal("file:///tmp/s", stack.Resource.CliEnvironment!["PULUMI_BACKEND_URL"]);
        Assert.NotEqual("file:///tmp/s", Environment.GetEnvironmentVariable("PULUMI_BACKEND_URL"));
    }

    [Fact]
    public void WithLocalBackend_makes_the_stack_local_and_uses_the_plain_stack_name()
    {
        var builder = DistributedApplication.CreateBuilder();
        var stack = builder.AddPulumiStack("infra", "dev").WithOrganization("acme").WithLocalBackend("/tmp/pulumi-state");
        Assert.True(stack.Resource.UsesLocalBackend);
        Assert.Equal("file:///tmp/pulumi-state", stack.Resource.BackendUrl);
        Assert.Equal("dev", stack.Resource.QualifiedStack);
    }

    [Fact]
    public void WithConfig_records_stack_config()
    {
        var builder = DistributedApplication.CreateBuilder();
        var stack = builder.AddPulumiStack("infra", "dev").WithConfig("azure-native:metadataHost", "localhost:4577");
        Assert.Equal("localhost:4577", stack.Resource.StackConfig["azure-native:metadataHost"]);
    }

    [Fact]
    public void WithProviderConfig_prefixes_the_key_with_the_provider()
    {
        var builder = DistributedApplication.CreateBuilder();
        var stack = builder.AddPulumiStack("infra", "dev").WithProviderConfig("azure-native", "metadataHost", "localhost:4577");
        Assert.Equal("localhost:4577", stack.Resource.StackConfig["azure-native:metadataHost"]);
    }

    [Fact]
    public void Secret_config_is_kept_out_of_the_plain_config_that_is_written_to_the_stack_file()
    {
        var builder = DistributedApplication.CreateBuilder();
        var stack = builder.AddPulumiStack("infra", "dev")
            .WithConfig("app:token", "plain")
            .WithProviderConfig("azure-native", "clientSecret", "s3cr3t", secret: true)
            .WithConfig("app:token", "now-secret", secret: true);
        Assert.Equal("s3cr3t", stack.Resource.StackSecretConfig["azure-native:clientSecret"]);
        Assert.Equal("now-secret", stack.Resource.StackSecretConfig["app:token"]);
        Assert.Empty(stack.Resource.StackConfig);
        var yaml = PulumiBackend.RenderStackFile(null, stack.Resource.StackConfig, null);
        Assert.DoesNotContain("s3cr3t", yaml);
        Assert.DoesNotContain("now-secret", yaml);
    }

    [Fact]
    public void Switching_a_secret_key_back_to_plain_removes_the_secret()
    {
        var builder = DistributedApplication.CreateBuilder();
        var stack = builder.AddPulumiStack("infra", "dev").WithConfig("a:b", "x", secret: true).WithConfig("a:b", "y");
        Assert.Empty(stack.Resource.StackSecretConfig);
        Assert.Equal("y", stack.Resource.StackConfig["a:b"]);
    }

    [Fact]
    public void Stack_file_for_a_local_backend_has_no_environment_import()
    {
        var yaml = PulumiBackend.RenderStackFile(null, new Dictionary<string, string> { ["azure-native:metadataHost"] = "localhost:4577" }, null);
        Assert.DoesNotContain("environment:", yaml);
        Assert.Contains("config:", yaml);
        Assert.Contains("\"azure-native:metadataHost\": \"localhost:4577\"", yaml);
    }

    [Fact]
    public void Stack_file_for_pulumi_cloud_imports_esc_environments()
    {
        var yaml = PulumiBackend.RenderStackFile(["app/dev"], new Dictionary<string, string>(), null);
        Assert.Equal("environment:\n  - app/dev\n", yaml.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Stack_file_rewrite_preserves_the_encryption_salt()
    {
        var existing = "encryptionsalt: v1:abc:xyz\nconfig:\n  a:b: c\n";
        var yaml = PulumiBackend.RenderStackFile(null, new Dictionary<string, string>(), existing);
        Assert.Contains("encryptionsalt: v1:abc:xyz", yaml);
    }

    [Fact]
    public void Stack_file_rewrite_preserves_kms_secrets_provider_keys()
    {
        var existing = "secretsprovider: azurekeyvault://v/k\nencryptedkey: abc\nconfig:\n  a:b:\n    secure: zzz\n";
        var yaml = PulumiBackend.RenderStackFile(null, new Dictionary<string, string>(), existing);
        Assert.Contains("secretsprovider: azurekeyvault://v/k", yaml);
        Assert.Contains("encryptedkey: abc", yaml);
        Assert.DoesNotContain("zzz", yaml);
    }

    [Fact]
    public void Empty_stack_file_is_valid_yaml()
    {
        Assert.Equal("{}\n", PulumiBackend.RenderStackFile(null, new Dictionary<string, string>(), null).Replace("\r\n", "\n"));
    }
}
