using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure.ServiceBus;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

public sealed class ServiceBusLocalRunTests
{
    [Fact]
    public void RunAsEmulator_adds_emulator_and_sql_containers_and_switches_the_connection_string()
    {
        var builder = DistributedApplication.CreateBuilder();
        var sb = builder.AddPulumiServiceBus("messaging").RunAsEmulator();
        var queue = sb.AddServiceBusQueue("orders");

        Assert.NotNull(sb.Resource.Emulator);
        Assert.Contains(builder.Resources, r => r.Name == "messaging-emulator");
        Assert.Contains(builder.Resources, r => r.Name == "messaging-mssql");

        // No stack is bound: the emulator form must not read any Pulumi stack output.
        var cs = sb.Resource.ConnectionStringExpression.ValueExpression;
        Assert.StartsWith("Endpoint=sb://", cs);
        Assert.Contains("UseDevelopmentEmulator=true", cs);
        Assert.DoesNotContain("outputs", cs);
        Assert.EndsWith(";EntityPath=orders", queue.Resource.ConnectionStringExpression.ValueExpression);
    }

    [Fact]
    public void RunAsEmulator_twice_throws()
    {
        var builder = DistributedApplication.CreateBuilder();
        var sb = builder.AddPulumiServiceBus("messaging").RunAsEmulator();
        Assert.Throws<InvalidOperationException>(() => sb.RunAsEmulator());
    }

    [Fact]
    public void Emulator_config_contains_queues_topics_subscriptions_and_rules()
    {
        var builder = DistributedApplication.CreateBuilder();
        var sb = builder.AddPulumiServiceBus("messaging");
        sb.AddServiceBusQueue("orders").WithProperties(q => { q.MaxDeliveryCount = 5; q.LockDuration = TimeSpan.FromMinutes(1); });
        var topic = sb.AddServiceBusTopic("events", "evt");
        topic.AddServiceBusSubscription("audit").WithProperties(s =>
        {
            var rule = new AzureServiceBusRule("only-new") { FilterType = AzureServiceBusFilterType.CorrelationFilter };
            rule.CorrelationFilter.Subject = "new";
            rule.CorrelationFilter.SendTo = "me";
            s.Rules.Add(rule);
        });

        var ns = sb.Resource.ToEmulatorConfig()["UserConfig"]!["Namespaces"]![0]!;
        Assert.Equal("messaging", (string?)ns["Name"]);
        var queue = ns["Queues"]![0]!;
        Assert.Equal("orders", (string?)queue["Name"]);
        Assert.Equal(5, (int?)queue["Properties"]!["MaxDeliveryCount"]);
        Assert.Equal("PT1M", (string?)queue["Properties"]!["LockDuration"]);
        var t = ns["Topics"]![0]!;
        Assert.Equal("evt", (string?)t["Name"]);
        var sub = t["Subscriptions"]![0]!;
        Assert.Equal("audit", (string?)sub["Name"]);
        var rule2 = sub["Rules"]![0]!["Properties"]!;
        Assert.Equal("Correlation", (string?)rule2["FilterType"]);
        Assert.Equal("new", (string?)rule2["CorrelationFilter"]!["Label"]);
        Assert.Equal("me", (string?)rule2["CorrelationFilter"]!["To"]);
    }

    [Fact]
    public void RunAsExisting_supplies_the_endpoint_in_run_mode_without_a_stack()
    {
        var builder = DistributedApplication.CreateBuilder();
        var sb = builder.AddPulumiServiceBus("messaging").AsExisting("shared-sb", "shared-rg")
            .RunAsExisting("shared-sb.servicebus.windows.net");
        var queue = sb.AddServiceBusQueue("orders");

        Assert.Equal("shared-sb.servicebus.windows.net", sb.Resource.ConnectionStringExpression.ValueExpression);
        Assert.Equal("Endpoint=shared-sb.servicebus.windows.net;EntityPath=orders", queue.Resource.ConnectionStringExpression.ValueExpression);
    }

    [Fact]
    public void RunAsExisting_accepts_a_parameter()
    {
        var builder = DistributedApplication.CreateBuilder();
        var endpoint = builder.AddParameter("sb-endpoint");
        var sb = builder.AddPulumiServiceBus("messaging").RunAsExisting(endpoint);
        Assert.Equal("{sb-endpoint.value}", sb.Resource.ConnectionStringExpression.ValueExpression);
    }
}
