using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class AnalyticsBoundaryTests
{
    [Fact]
    public void Api_host_does_not_register_agent_core_skills()
    {
        var programSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "api", "Clawbot.Api", "Program.cs"));

        programSource.Should().NotContain("AddClawbotSkills");
    }
}

