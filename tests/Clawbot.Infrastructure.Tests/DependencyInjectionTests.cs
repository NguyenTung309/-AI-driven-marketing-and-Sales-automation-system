using Clawbot.Agents.Core.Lead;
using Clawbot.Infrastructure;
using Clawbot.Infrastructure.Content.Publishing;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Clawbot.Infrastructure.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddInfrastructure_registers_lead_assignment_for_api_endpoints_and_consumers()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure(TestConfig());

        services.Should().Contain(d =>
            d.ServiceType == typeof(ILeadAssignmentService)
            && d.ImplementationType == typeof(LeastBusyLeadAssignmentService));
    }

    [Fact]
    public void AddInfrastructure_keeps_webhook_publisher_when_graph_sections_disabled()
    {
        var cfg = TestConfig(new Dictionary<string, string?>
        {
            ["Content:GraphPublisher:Facebook:Enabled"] = "false",
            ["Content:GraphPublisher:Zalo:Enabled"] = "false",
        });
        var services = new ServiceCollection();

        services.AddInfrastructure(cfg);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ISocialPublisher>().Should().BeOfType<HttpSocialPublisher>();
    }

    [Fact]
    public void AddInfrastructure_uses_graph_publisher_when_any_graph_channel_enabled()
    {
        var cfg = TestConfig(new Dictionary<string, string?>
        {
            ["Content:GraphPublisher:Facebook:Enabled"] = "true",
        });
        var services = new ServiceCollection();

        services.AddInfrastructure(cfg);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ISocialPublisher>().Should().BeOfType<GraphSocialPublisher>();
    }

    private static IConfiguration TestConfig(Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:SqlServer"] = "Server=localhost;Database=clawbot_test;Trusted_Connection=True;TrustServerCertificate=True",
            ["ConnectionStrings:Redis"] = "localhost:6379",
            ["Encryption:Base64Key"] = Convert.ToBase64String(new byte[32]),
        };
        if (overrides is not null)
            foreach (var (key, value) in overrides)
                values[key] = value;

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
