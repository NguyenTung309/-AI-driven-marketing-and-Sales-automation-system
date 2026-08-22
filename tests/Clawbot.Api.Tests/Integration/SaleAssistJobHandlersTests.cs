using System.Text.Json;
using Clawbot.Agents.Contracts.SaleAssist;
using Clawbot.Api.Jobs;
using Clawbot.SharedKernel.Jobs;
using FluentAssertions;
using Grpc.Core;
using NSubstitute;

namespace Clawbot.Api.Tests.Integration;

// Unit test thuần (không qua ApiTestFactory/HTTP host) cho SaleAssistDraftJobHandler
// (saleassist.draft) và SaleAssistSummaryJobHandler (saleassist.summary). Bỏ qua
// SaleAssistUpsellJobHandler ở đây vì nó còn phụ thuộc AppDbContext (đọc/ghi UpsellSuggestionCache) —
// coverage cho nhánh cache đó nằm ở SaleAssistUpsellSuggestionServiceTests, chỗ này chỉ cần 2 handler
// đơn giản chỉ gọi gRPC rồi serialize thẳng ra JobResult.Summary.
public sealed class SaleAssistJobHandlersTests
{
    [Fact]
    public async Task SaleAssistDraftJobHandler_ValidPayload_ReturnsInboxLinkAndDraftJsonSummary()
    {
        var grpc = Substitute.For<SaleAssistAgent.SaleAssistAgentClient>();
        var response = new DraftResponse
        {
            DraftText = "Xin chào anh/chị, Học Bá xin gửi báo giá gói Premium.",
            SuggestedAction = "send_quote",
            LeadScore = 72,
        };
        grpc.DraftAsync(Arg.Any<DraftRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(CompletedUnaryCall(response));

        var handler = new SaleAssistDraftJobHandler(grpc);
        var conversationId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var payload = new SaleAssistConversationJobPayload(conversationId);
        var ctx = new JobContext(
            Guid.NewGuid(),
            tenantId,
            Guid.NewGuid(),
            JsonSerializer.Serialize(payload),
            new NoopJobProgress());

        var result = await handler.RunAsync(ctx, CancellationToken.None);

        result.ResultLink.Should().Be($"/inbox?conversation={conversationId}");
        using var doc = JsonDocument.Parse(result.Summary!);
        doc.RootElement.GetProperty("draftText").GetString()
            .Should().Be("Xin chào anh/chị, Học Bá xin gửi báo giá gói Premium.");
        doc.RootElement.GetProperty("suggestedAction").GetString().Should().Be("send_quote");
        doc.RootElement.GetProperty("leadScoreHint").GetInt32().Should().Be(72);
    }

    [Fact]
    public async Task SaleAssistDraftJobHandler_ValidPayload_SendsRequestWithTenantConversationAndSaleUser()
    {
        var grpc = Substitute.For<SaleAssistAgent.SaleAssistAgentClient>();
        grpc.DraftAsync(Arg.Any<DraftRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(CompletedUnaryCall(new DraftResponse()));

        var handler = new SaleAssistDraftJobHandler(grpc);
        var conversationId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var saleUserId = Guid.NewGuid();
        var payload = new SaleAssistConversationJobPayload(conversationId);
        var ctx = new JobContext(
            Guid.NewGuid(),
            tenantId,
            saleUserId,
            JsonSerializer.Serialize(payload),
            new NoopJobProgress());

        await handler.RunAsync(ctx, CancellationToken.None);

        _ = grpc.Received(1).DraftAsync(
            Arg.Is<DraftRequest>(req =>
                req.TenantId == tenantId.ToString() &&
                req.ConversationId == conversationId.ToString() &&
                req.SaleUserId == saleUserId.ToString()),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaleAssistDraftJobHandler_MissingPayload_ThrowsInvalidOperationException()
    {
        var grpc = Substitute.For<SaleAssistAgent.SaleAssistAgentClient>();
        var handler = new SaleAssistDraftJobHandler(grpc);
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
    public async Task SaleAssistSummaryJobHandler_ValidPayload_ReturnsInboxLinkAndSummaryJson()
    {
        var grpc = Substitute.For<SaleAssistAgent.SaleAssistAgentClient>();
        var response = new SummarizeResponse { Summary = "Khách quan tâm gói Premium, hẹn gọi lại chiều mai." };
        grpc.SummarizeAsync(Arg.Any<SummarizeRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(CompletedUnaryCall(response));

        var handler = new SaleAssistSummaryJobHandler(grpc);
        var conversationId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var payload = new SaleAssistConversationJobPayload(conversationId);
        var ctx = new JobContext(
            Guid.NewGuid(),
            tenantId,
            Guid.NewGuid(),
            JsonSerializer.Serialize(payload),
            new NoopJobProgress());

        var result = await handler.RunAsync(ctx, CancellationToken.None);

        result.ResultLink.Should().Be($"/inbox?conversation={conversationId}");
        using var doc = JsonDocument.Parse(result.Summary!);
        doc.RootElement.GetProperty("summary").GetString()
            .Should().Be("Khách quan tâm gói Premium, hẹn gọi lại chiều mai.");
    }

    [Fact]
    public async Task SaleAssistSummaryJobHandler_ValidPayload_SendsRequestWithTenantAndConversation()
    {
        var grpc = Substitute.For<SaleAssistAgent.SaleAssistAgentClient>();
        grpc.SummarizeAsync(Arg.Any<SummarizeRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(CompletedUnaryCall(new SummarizeResponse()));

        var handler = new SaleAssistSummaryJobHandler(grpc);
        var conversationId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var payload = new SaleAssistConversationJobPayload(conversationId);
        var ctx = new JobContext(
            Guid.NewGuid(),
            tenantId,
            Guid.NewGuid(),
            JsonSerializer.Serialize(payload),
            new NoopJobProgress());

        await handler.RunAsync(ctx, CancellationToken.None);

        _ = grpc.Received(1).SummarizeAsync(
            Arg.Is<SummarizeRequest>(req =>
                req.TenantId == tenantId.ToString() &&
                req.ConversationId == conversationId.ToString()),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaleAssistSummaryJobHandler_MissingPayload_ThrowsInvalidOperationException()
    {
        var grpc = Substitute.For<SaleAssistAgent.SaleAssistAgentClient>();
        var handler = new SaleAssistSummaryJobHandler(grpc);
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
