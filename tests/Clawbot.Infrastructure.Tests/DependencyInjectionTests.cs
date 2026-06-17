using Clawbot.Agents.Core.Lead;
using Clawbot.Infrastructure;
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
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = "Server=localhost;Database=clawbot_test;Trusted_Connection=True;TrustServerCertificate=True",
                ["ConnectionStrings:Redis"] = "localhost:6379",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddInfrastructure(cfg);

        services.Should().Contain(d =>
            d.ServiceType == typeof(ILeadAssignmentService)
            && d.ImplementationType == typeof(LeastBusyLeadAssignmentService));
    }
}
