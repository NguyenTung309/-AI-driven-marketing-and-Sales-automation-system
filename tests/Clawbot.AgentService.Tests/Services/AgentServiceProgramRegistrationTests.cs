using FluentAssertions;

namespace Clawbot.AgentService.Tests.Services;

public sealed class AgentServiceProgramRegistrationTests
{
    [Fact]
    public void Agent_service_program_uses_default_orchestrator_registry()
    {
        var program = File.ReadAllText(FindRepoFile("src", "agents", "Clawbot.AgentService", "Program.cs"));

        program.Should().Contain("DefaultAgentRegistry.Create()");
        program.Should().NotContain("Array.Empty<IAgent>()");
    }

    [Fact]
    public void Agent_service_program_registers_schedule_worker()
    {
        var program = File.ReadAllText(FindRepoFile("src", "agents", "Clawbot.AgentService", "Program.cs"));

        program.Should().Contain("AddScoped<Clawbot.AgentService.Services.AgentScheduleRunner>()");
        program.Should().Contain("AddHostedService<Clawbot.AgentService.Services.AgentScheduleWorker>()");
    }

    [Fact]
    public void Agent_service_program_registers_content_review_dispatch_pipeline()
    {
        var program = File.ReadAllText(FindRepoFile("src", "agents", "Clawbot.AgentService", "Program.cs"));

        program.Should().Contain("Configure<Clawbot.AgentService.Services.ContentReviewWorkerOptions>");
        program.Should().Contain("AddScoped<Clawbot.AgentService.Services.IContentReviewExecutor");
        program.Should().Contain("AddScoped<Clawbot.AgentService.Services.IContentReviewCoordinator");
        program.Should().Contain("AddScoped<Clawbot.AgentService.Services.IContentPublishingApprovalPolicyResolver");
        program.Should().Contain("AddScoped<Clawbot.AgentService.Services.ReviewTenantWorker>()");
        program.Should().Contain("AddScoped<Clawbot.AgentService.Services.IReviewTenantRunner>");
        program.Should().Contain("AddHostedService<Clawbot.AgentService.Services.ContentReviewDispatchWorker>()");
    }

    private static string FindRepoFile(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(segments)}");
    }
}
