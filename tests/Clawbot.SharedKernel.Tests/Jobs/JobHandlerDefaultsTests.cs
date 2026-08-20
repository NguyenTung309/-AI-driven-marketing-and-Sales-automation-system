using Clawbot.SharedKernel.Jobs;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Jobs;

public sealed class JobHandlerDefaultsTests
{
    [Fact]
    public void NotifyOnSuccess_DefaultsToTrue()
    {
        IJobHandler handler = new DefaultNotifyHandler();

        handler.NotifyOnSuccess.Should().BeTrue();
    }

    [Fact]
    public void NotifyOnSuccess_CanBeOptedOutForInteractiveJobs()
    {
        // Việc tương tác (user ngồi chờ kết quả) tắt thông báo thành công để khỏi spam chuông.
        IJobHandler handler = new SilentHandler();

        handler.NotifyOnSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_ReturnsHandlerResult()
    {
        IJobHandler handler = new DefaultNotifyHandler();
        var ctx = new JobContext(Guid.NewGuid(), Guid.NewGuid(), null, "{}", new NoopProgress());

        var result = await handler.RunAsync(ctx, CancellationToken.None);

        result.Summary.Should().Be("done");
    }

    private sealed class DefaultNotifyHandler : IJobHandler
    {
        public string Type => "test.default";

        public Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct) =>
            Task.FromResult(new JobResult(Summary: "done"));
    }

    private sealed class SilentHandler : IJobHandler
    {
        public string Type => "test.silent";

        public bool NotifyOnSuccess => false;

        public Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct) =>
            Task.FromResult(new JobResult());
    }

    private sealed class NoopProgress : IJobProgress
    {
        public Task ReportAsync(int percent, string? note, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
