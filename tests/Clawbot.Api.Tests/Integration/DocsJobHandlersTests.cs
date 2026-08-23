using System.Text.Json;
using Clawbot.Agents.Contracts.Docs;
using Clawbot.Agents.Core.Docs;
using Clawbot.Api.Jobs;
using Clawbot.Api.Services;
using Clawbot.Application.Abstractions;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Clawbot.Api.Tests.Integration;

// Unit test thuần (không qua ApiTestFactory/HTTP host) cho DocsGenerateJobHandler (docs.generate).
// DocumentDeliveryService là 1 class cụ thể (không phải interface) và KHÔNG có method virtual/không
// sealed=false với override point nào để NSubstitute.ForPartsOf giả lập nhánh gửi (SentVia có giá trị)
// một cách chắc chắn — nên chỉ dựng instance THẬT với dependency giả (IEmailSender/IDocumentStorage/
// IClock substitute, AppDbContext SQLite in-memory, không IChannelAdapter nào) và chỉ test nhánh
// SentVia null/rỗng (không gọi delivery). Nhánh SentVia có giá trị (gửi email/zalo thật) bị bỏ qua ở
// đây, đã có coverage riêng cho DocumentDeliveryService ở chỗ khác nếu cần.
public sealed class DocsJobHandlersTests
{
    [Fact]
    public async Task DocsGenerateJobHandler_PayloadWithoutSentVia_ReturnsDocumentsLinkAndSummaryWithTemplateAndSize()
    {
        var grpc = Substitute.For<Clawbot.Agents.Contracts.Docs.DocsAgent.DocsAgentClient>();
        var documentId = Guid.NewGuid();
        var response = new DocGenerateResponse
        {
            DocumentId = documentId.ToString(),
            FileUrl = "/generated-docs/hop-dong.pdf",
            FileHash = "sha256-abc",
            SizeBytes = 4096,
            LatencyMs = 250,
        };
        grpc.GenerateAsync(Arg.Any<DocGenerateRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(CompletedUnaryCall(response));

        await using var fixture = await DeliveryFixture.CreateAsync();
        var handler = new DocsGenerateJobHandler(grpc, fixture.Delivery);

        var tenantId = Guid.NewGuid();
        var payload = new DocsGenerateJobPayload("HOP_DONG_MAU", null, null, null, null);
        var ctx = new JobContext(
            Guid.NewGuid(),
            tenantId,
            Guid.NewGuid(),
            JsonSerializer.Serialize(payload),
            new NoopJobProgress());

        var result = await handler.RunAsync(ctx, CancellationToken.None);

        result.ResultLink.Should().Be("/documents");
        result.Summary.Should().Contain("HOP_DONG_MAU");
        result.Summary.Should().Contain("4096");
    }

    [Fact]
    public async Task DocsGenerateJobHandler_PayloadWithoutSentVia_DoesNotCallDelivery()
    {
        var grpc = Substitute.For<Clawbot.Agents.Contracts.Docs.DocsAgent.DocsAgentClient>();
        var response = new DocGenerateResponse
        {
            DocumentId = Guid.NewGuid().ToString(),
            FileUrl = "/generated-docs/hop-dong.pdf",
            FileHash = "sha256-abc",
            SizeBytes = 1024,
            LatencyMs = 100,
        };
        grpc.GenerateAsync(Arg.Any<DocGenerateRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(CompletedUnaryCall(response));

        await using var fixture = await DeliveryFixture.CreateAsync();
        var handler = new DocsGenerateJobHandler(grpc, fixture.Delivery);

        var payload = new DocsGenerateJobPayload("BAO_GIA", null, null, SentVia: null, RecipientEmail: null);
        var ctx = new JobContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            JsonSerializer.Serialize(payload),
            new NoopJobProgress());

        await handler.RunAsync(ctx, CancellationToken.None);

        // SentVia null -> DocumentsEndpoints.GenerateOneAsync không gọi nhánh delivery.TrySendAsync,
        // nên không có call nào phát sinh trên bất kỳ dependency nào của DocumentDeliveryService.
        fixture.Email.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task DocsGenerateJobHandler_ValidPayload_SendsRequestWithTenantTemplateAndVars()
    {
        var grpc = Substitute.For<Clawbot.Agents.Contracts.Docs.DocsAgent.DocsAgentClient>();
        var response = new DocGenerateResponse
        {
            DocumentId = Guid.NewGuid().ToString(),
            FileUrl = "/generated-docs/hop-dong.pdf",
            FileHash = "sha256-abc",
            SizeBytes = 2048,
            LatencyMs = 150,
        };
        grpc.GenerateAsync(Arg.Any<DocGenerateRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(CompletedUnaryCall(response));

        await using var fixture = await DeliveryFixture.CreateAsync();
        var handler = new DocsGenerateJobHandler(grpc, fixture.Delivery);

        var tenantId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var vars = new Dictionary<string, string> { ["ho_ten"] = "Nguyễn Văn A" };
        var payload = new DocsGenerateJobPayload("HOP_DONG_MAU", contactId, vars, null, null);
        var ctx = new JobContext(
            Guid.NewGuid(),
            tenantId,
            Guid.NewGuid(),
            JsonSerializer.Serialize(payload),
            new NoopJobProgress());

        await handler.RunAsync(ctx, CancellationToken.None);

        _ = grpc.Received(1).GenerateAsync(
            Arg.Is<DocGenerateRequest>(req =>
                req.TenantId == tenantId.ToString() &&
                req.ContactId == contactId.ToString() &&
                req.TemplateCode == "HOP_DONG_MAU" &&
                req.Vars["ho_ten"] == "Nguyễn Văn A" &&
                req.SentVia == string.Empty),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DocsGenerateJobHandler_MissingPayload_ThrowsInvalidOperationException()
    {
        var grpc = Substitute.For<Clawbot.Agents.Contracts.Docs.DocsAgent.DocsAgentClient>();
        await using var fixture = await DeliveryFixture.CreateAsync();
        var handler = new DocsGenerateJobHandler(grpc, fixture.Delivery);

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

    // Dựng DocumentDeliveryService thật với toàn bộ dependency giả — class này sealed nên không thể
    // NSubstitute.ForPartsOf được, buộc phải new instance thật để tiêm vào DocsGenerateJobHandler.
    private sealed class DeliveryFixture(
        SqliteConnection connection,
        AppDbContext db,
        IEmailSender email) : IAsyncDisposable
    {
        public DocumentDeliveryService Delivery { get; } = new(
            db,
            email,
            Array.Empty<IChannelAdapter>(),
            Substitute.For<IDocumentStorage>(),
            new DocsStorageOptions(),
            Substitute.For<IClock>());

        public IEmailSender Email { get; } = email;

        public static async Task<DeliveryFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AppDbContext(options, new NullTenantAccessor());
            await db.Database.EnsureCreatedAsync();
            return new DeliveryFixture(connection, db, Substitute.For<IEmailSender>());
        }

        public async ValueTask DisposeAsync()
        {
            await db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public TenantContext? Current => null;

        public TenantContext Require() =>
            throw new InvalidOperationException("No tenant in unit test scope.");
    }
}
