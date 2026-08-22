using System.Text.Json;
using Clawbot.Agents.Contracts.Content;
using Clawbot.Agents.Contracts.Research;
using Clawbot.Api.Jobs;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Grpc.Core;
using NSubstitute;

namespace Clawbot.Api.Tests.Integration;

// Unit test thuần (không qua ApiTestFactory/HTTP host) cho 2 job handler chạy qua Hangfire, không
// có route HTTP riêng: ContentRepurposeJobHandler (content.repurpose) và ContentTrendScanJobHandler
// (content.trends-scan). gRPC generated client có method virtual nên NSubstitute mock trực tiếp được.
public sealed class ContentJobHandlersTests
{
    [Fact]
    public async Task ContentRepurposeJobHandler_ValidPayload_ReturnsSummaryWithVariantCountAndQueueLink()
    {
        var grpc = Substitute.For<ContentAgent.ContentAgentClient>();
        var contentItemId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var response = new ContentResponse();
        response.Variants.Add(new ContentVariant { Platform = "facebook", Title = "t1", Body = "b1" });
        response.Variants.Add(new ContentVariant { Platform = "tiktok", Title = "t2", Body = "b2" });

        grpc.RepurposeAsync(Arg.Any<RepurposeRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(CompletedUnaryCall(response));

        var handler = new ContentRepurposeJobHandler(grpc);
        var payload = new ContentRepurposeJobPayload(contentItemId, ["facebook", "tiktok"]);
        var ctx = new JobContext(
            Guid.NewGuid(),
            tenantId,
            Guid.NewGuid(),
            JsonSerializer.Serialize(payload),
            new NoopJobProgress());

        var result = await handler.RunAsync(ctx, CancellationToken.None);

        result.Summary.Should().Contain("2");
        result.ResultLink.Should().Be("/content?tab=queue");
    }

    [Fact]
    public async Task ContentRepurposeJobHandler_ValidPayload_SendsRequestWithTenantContentAndChannels()
    {
        var grpc = Substitute.For<ContentAgent.ContentAgentClient>();
        var contentItemId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        grpc.RepurposeAsync(Arg.Any<RepurposeRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(CompletedUnaryCall(new ContentResponse()));

        var handler = new ContentRepurposeJobHandler(grpc);
        var payload = new ContentRepurposeJobPayload(contentItemId, ["facebook", "tiktok"]);
        var ctx = new JobContext(
            Guid.NewGuid(),
            tenantId,
            Guid.NewGuid(),
            JsonSerializer.Serialize(payload),
            new NoopJobProgress());

        await handler.RunAsync(ctx, CancellationToken.None);

        _ = grpc.Received(1).RepurposeAsync(
            Arg.Is<RepurposeRequest>(req =>
                req.TenantId == tenantId.ToString() &&
                req.ContentId == contentItemId.ToString() &&
                req.TargetChannels.Count == 2 &&
                req.TargetChannels.Contains("facebook") &&
                req.TargetChannels.Contains("tiktok")),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContentRepurposeJobHandler_MissingPayload_ThrowsInvalidOperationException()
    {
        var grpc = Substitute.For<ContentAgent.ContentAgentClient>();
        var handler = new ContentRepurposeJobHandler(grpc);
        var ctx = new JobContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "null",
            new NoopJobProgress());

        var act = async () => await handler.RunAsync(ctx, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ContentTrendScanJobHandler_ValidPayload_ReturnsSummaryWithWeekAndTrendCount()
    {
        var grpc = Substitute.For<ResearchAgent.ResearchAgentClient>();
        var notifier = Substitute.For<IContentNotifier>();
        var clock = Substitute.For<IClock>();
        var now = new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);
        clock.UtcNow.Returns(now);

        var response = new TrendResponse();
        response.Trends.Add(new TrendItem { Topic = "topic1", Source = "tiktok" });
        response.Trends.Add(new TrendItem { Topic = "topic2", Source = "google_trends" });
        response.Trends.Add(new TrendItem { Topic = "topic3", Source = "youtube" });

        grpc.WeeklyTrendsAsync(Arg.Any<TrendRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(CompletedUnaryCall(response));

        var handler = new ContentTrendScanJobHandler(grpc, notifier, clock);
        var tenantId = Guid.NewGuid();
        const string weekOf = "2026-W34";
        var payload = new ContentTrendScanJobPayload(weekOf);
        var ctx = new JobContext(
            Guid.NewGuid(),
            tenantId,
            Guid.NewGuid(),
            JsonSerializer.Serialize(payload),
            new NoopJobProgress());

        var result = await handler.RunAsync(ctx, CancellationToken.None);

        result.Summary.Should().Contain(weekOf);
        result.Summary.Should().Contain("3");
        result.ResultLink.Should().Be("/content?tab=trends");
    }

    [Fact]
    public async Task ContentTrendScanJobHandler_ValidPayload_NotifiesTenantExactlyOnceWithTrendCountAndClockTime()
    {
        var grpc = Substitute.For<ResearchAgent.ResearchAgentClient>();
        var notifier = Substitute.For<IContentNotifier>();
        var clock = Substitute.For<IClock>();
        var now = new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);
        clock.UtcNow.Returns(now);

        var response = new TrendResponse();
        response.Trends.Add(new TrendItem { Topic = "topic1", Source = "tiktok" });

        grpc.WeeklyTrendsAsync(Arg.Any<TrendRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(CompletedUnaryCall(response));

        var handler = new ContentTrendScanJobHandler(grpc, notifier, clock);
        var tenantId = Guid.NewGuid();
        var payload = new ContentTrendScanJobPayload("2026-W34");
        var ctx = new JobContext(
            Guid.NewGuid(),
            tenantId,
            Guid.NewGuid(),
            JsonSerializer.Serialize(payload),
            new NoopJobProgress());

        await handler.RunAsync(ctx, CancellationToken.None);

        await notifier.Received(1).NotifyTrendScanAsync(
            tenantId,
            Arg.Is<ContentTrendScanEvent>(evt =>
                evt.TenantId == tenantId &&
                evt.TrendCount == 1 &&
                evt.OccurredAt == now),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContentTrendScanJobHandler_MissingPayload_ThrowsInvalidOperationException()
    {
        var grpc = Substitute.For<ResearchAgent.ResearchAgentClient>();
        var notifier = Substitute.For<IContentNotifier>();
        var clock = Substitute.For<IClock>();
        var handler = new ContentTrendScanJobHandler(grpc, notifier, clock);
        var ctx = new JobContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "null",
            new NoopJobProgress());

        var act = async () => await handler.RunAsync(ctx, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static AsyncUnaryCall<T> CompletedUnaryCall<T>(T response) where T : class =>
        new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    private sealed class NoopJobProgress : IJobProgress
    {
        public Task ReportAsync(int percent, string? note, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
