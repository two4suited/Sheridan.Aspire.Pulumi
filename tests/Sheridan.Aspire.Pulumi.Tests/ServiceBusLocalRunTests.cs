using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Sheridan.Aspire.Pulumi.Azure.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
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

    private static IDistributedApplicationBuilder PublishBuilder() =>
        DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = ["--operation", "publish", "--output-path", Path.GetTempPath()] });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RunAsEmulator_and_RunAsExisting_exclude_each_other_in_both_orders_and_modes(bool publish)
    {
        IDistributedApplicationBuilder New() => publish ? PublishBuilder() : DistributedApplication.CreateBuilder();

        var b1 = New();
        var sb1 = b1.AddPulumiServiceBus("a").RunAsEmulator();
        Assert.Throws<InvalidOperationException>(() => sb1.RunAsExisting("x.servicebus.windows.net"));

        var b2 = New();
        var sb2 = b2.AddPulumiServiceBus("a").RunAsExisting("x.servicebus.windows.net");
        Assert.Throws<InvalidOperationException>(() => sb2.RunAsEmulator());
    }

    [Fact]
    public void Namespace_and_entities_have_mail_icons()
    {
        var builder = DistributedApplication.CreateBuilder();
        var sb = builder.AddPulumiServiceBus("messaging");
        var queue = sb.AddServiceBusQueue("orders");
        var topic = sb.AddServiceBusTopic("events");
        var sub = topic.AddServiceBusSubscription("audit");

        Assert.Equal("MailMultiple", sb.Resource.Annotations.OfType<ResourceIconAnnotation>().Single().IconName);
        Assert.Equal("Mail", queue.Resource.Annotations.OfType<ResourceIconAnnotation>().Single().IconName);
        Assert.Equal("Mail", topic.Resource.Annotations.OfType<ResourceIconAnnotation>().Single().IconName);
        Assert.Equal("Mail", sub.Resource.Annotations.OfType<ResourceIconAnnotation>().Single().IconName);
    }

    [Fact]
    public void WithSqlServer_callback_customizes_the_sql_container_and_ties_its_lifetime_to_the_emulator()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.AddPulumiServiceBus("messaging").RunAsEmulator(e => e
            .WithSqlServer()
            .WithSqlServer(sql => sql.WithContainerName("custom-sql").WithHostPort(14330)));

        var sql = Assert.Single(builder.Resources.OfType<SqlServerServerResource>());
        Assert.Equal("messaging-mssql", sql.Name);
        Assert.Equal("custom-sql", sql.Annotations.OfType<ContainerNameAnnotation>().Single().Name);
        Assert.Equal(14330, sql.Annotations.OfType<EndpointAnnotation>().First(a => a.Name == "tcp").Port);
        var emulator = Assert.Single(builder.Resources.OfType<AzureServiceBusEmulatorResource>());
        Assert.Contains(emulator.Annotations.OfType<WaitAnnotation>(), w => w.Resource == sql);
        Assert.Contains(sql.Annotations, a => a.GetType().Name == "PersistenceAnnotation");
    }

    [Fact]
    public void WithSqlServer_existing_reuses_the_server_and_creates_no_sql_container()
    {
        var builder = DistributedApplication.CreateBuilder();
        var shared = builder.AddSqlServer("shared-sql");
        var sb = builder.AddPulumiServiceBus("messaging").RunAsEmulator(e => e.WithSqlServer(shared));

        Assert.Single(builder.Resources.OfType<SqlServerServerResource>());
        Assert.Same(shared.Resource, sb.Resource.Emulator!.SqlServer);
        Assert.Contains(sb.Resource.Emulator.Annotations.OfType<WaitAnnotation>(), w => w.Resource == shared.Resource);
    }

    [Fact]
    public void WithSqlServer_existing_last_call_wins()
    {
        var builder = DistributedApplication.CreateBuilder();
        var a = builder.AddSqlServer("sql-a");
        var b = builder.AddSqlServer("sql-b");
        var sb = builder.AddPulumiServiceBus("messaging").RunAsEmulator(e => e.WithSqlServer(a).WithSqlServer(b));

        Assert.Same(b.Resource, sb.Resource.Emulator!.SqlServer);
        Assert.DoesNotContain(sb.Resource.Emulator.Annotations.OfType<WaitAnnotation>(), w => w.Resource == a.Resource);
    }

    [Fact]
    public void WithSqlServer_callback_and_existing_conflict_in_both_orders()
    {
        var builder = DistributedApplication.CreateBuilder();
        var shared = builder.AddSqlServer("shared-sql");
        Assert.Throws<InvalidOperationException>(() =>
            builder.AddPulumiServiceBus("a").RunAsEmulator(e => e.WithSqlServer(_ => { }).WithSqlServer(shared)));
        Assert.Throws<InvalidOperationException>(() =>
            builder.AddPulumiServiceBus("b").RunAsEmulator(e => e.WithSqlServer(shared).WithSqlServer(_ => { })));
    }

    [Fact]
    public void WithSqlServer_callback_removing_the_tcp_endpoint_throws()
    {
        var builder = DistributedApplication.CreateBuilder();
        Assert.Throws<InvalidOperationException>(() =>
            builder.AddPulumiServiceBus("a").RunAsEmulator(e => e.WithSqlServer(sql => sql.Resource.Annotations.Clear())));
    }

    [Fact]
    public void Resources_referencing_a_queue_wait_for_that_queue()
    {
        var builder = DistributedApplication.CreateBuilder();
        var sb = builder.AddPulumiServiceBus("messaging").RunAsEmulator();
        var queue = sb.AddServiceBusQueue("orders");
        var other = sb.AddServiceBusQueue("other");
        var app = builder.AddContainer("worker", "img").WithReference(queue);

        ServiceBusStateInitializer.AddWaits(builder.Resources, sb.Resource);

        var waits = app.Resource.Annotations.OfType<WaitAnnotation>().Select(w => w.Resource).ToList();
        Assert.Contains(queue.Resource, waits);
        Assert.DoesNotContain(other.Resource, waits);
        Assert.DoesNotContain(sb.Resource.Emulator, waits);
    }

    [Fact]
    public async Task Emulator_ready_publishes_Running_for_namespace_and_entities()
    {
        var builder = DistributedApplication.CreateBuilder();
        var sb = builder.AddPulumiServiceBus("messaging").RunAsEmulator();
        sb.AddServiceBusQueue("orders");
        sb.AddServiceBusTopic("events").AddServiceBusSubscription("audit");

        using var app = builder.Build();
        var notifications = app.Services.GetRequiredService<ResourceNotificationService>();

        await builder.Eventing.PublishAsync(new ResourceReadyEvent(sb.Resource.Emulator!, app.Services), CancellationToken.None);

        foreach (var r in ServiceBusStateInitializer.Related(sb.Resource))
        {
            Assert.True(notifications.TryGetCurrentState(r.Name, out var ev));
            Assert.Equal(KnownResourceStates.Running, ev!.Snapshot.State?.Text);
        }
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
        Assert.Equal("sbemulatorns", (string?)ns["Name"]);
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
