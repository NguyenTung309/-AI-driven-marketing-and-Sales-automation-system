using Clawbot.SharedKernel.Jobs;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Jobs;

public sealed class JobContractRecordTests
{
    [Fact]
    public void JobContext_SetsAllFields()
    {
        var jobId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var progress = new StubProgress();

        var ctx = new JobContext(jobId, tenantId, userId, "{\"key\":\"val\"}", progress);

        ctx.JobId.Should().Be(jobId);
        ctx.TenantId.Should().Be(tenantId);
        ctx.UserId.Should().Be(userId);
        ctx.PayloadJson.Should().Be("{\"key\":\"val\"}");
        ctx.Progress.Should().BeSameAs(progress);
    }

    [Fact]
    public void JobResult_DefaultValues()
    {
        var result = new JobResult();

        result.ResultLink.Should().BeNull();
        result.Summary.Should().BeNull();
    }

    [Fact]
    public void JobResult_WithValues()
    {
        var result = new JobResult("/jobs/123", "Completed 5 items");

        result.ResultLink.Should().Be("/jobs/123");
        result.Summary.Should().Be("Completed 5 items");
    }

    private sealed class StubProgress : IJobProgress
    {
        public Task ReportAsync(int percent, string? note, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
