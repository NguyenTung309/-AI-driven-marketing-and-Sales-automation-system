using Clawbot.Agents.Core.Skills;
using Clawbot.Agents.Core.Skills.Ops;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Clawbot.Api.Tests;

public sealed class LeadsForecastRegistrationTests
{
    [Fact]
    public void AddClawbotForecasting_registers_forecaster_for_leads_forecast_endpoint()
    {
        var services = new ServiceCollection();

        services.AddClawbotForecasting();

        services.Should().Contain(d => d.ServiceType == typeof(IForecaster));
    }

    [Fact]
    public void Api_startup_calls_forecasting_registration()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "src", "api", "Clawbot.Api", "Program.cs"));

        program.Should().Contain("AddClawbotForecasting");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Clawbot.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
