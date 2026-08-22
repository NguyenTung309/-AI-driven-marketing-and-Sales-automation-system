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

// Unit test thuần cho DocsKitJobHandler (docs.generate-kit) — file riêng với class riêng, không trùng DocsJobHandlersTests.
public sealed class DocsKitJobHandlerTests
{
    [Fact]
    public async Task RunAsync_TwoTemplatesBothSucceed_ReturnsTwoDocsAndReportsProgress()
    {
        var grpc = Substitute.For<Clawbot.Agents.Contracts.Docs.DocsAgent.DocsAgentClient>();
        grpc.GenerateAsync(Arg.Any<DocGenerateRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(CompletedUnaryCall(new DocGenerateResponse
            {
                DocumentId = Guid.NewGuid().ToString(),
                FileUrl = "/generated-docs/a.pdf",
                FileHash = "h1",
                SizeBytes = 1024,
                LatencyMs = 100,
            }));

        await using var fixture = await DocsKitFixture.CreateAsync();
        var handler = new DocsKitJobHandler(grpc, fixture.Delivery);
        var progress = Substitute.For<IJobProgress>();
        var tenantId = Guid.NewGuid();
        var payload = new DocsKitJobPayload(["HOP_DONG", "BAO_GIA"], null, null, null, null);
        var ctx = new JobContext(Guid.NewGuid(), tenantId, Guid.NewGuid(), JsonSerializer.Serialize(payload), progress);

        var result = await handler.RunAsync(ctx, CancellationToken.None);

        result.ResultLink.Should().Be("/documents");
        result.Summary.Should().Contain("2");
        result.Summary.Should().Contain("Đã sinh");
        await progress.Received(2).ReportAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_SecondTemplateNotFound_SkipsItAndReportsPartialSummary()
    {
        var grpc = Substitute.For<Clawbot.Agents.Contracts.Docs.DocsAgent.DocsAgentClient>();
        var callCount = 0;
        grpc.GenerateAsync(Arg.Any<DocGenerateRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 1)
                    return CompletedUnaryCall(new DocGenerateResponse
                    {
                        DocumentId = Guid.NewGuid().ToString(),
                        FileUrl = "/generated-docs/a.pdf",
                        FileHash = "h1",
                        SizeBytes = 2048,
                        LatencyMs = 100,
                    });
                throw new RpcException(new Status(StatusCode.NotFound, "template not found"));
            });

        await using var fixture = await DocsKitFixture.CreateAsync();
        var handler = new DocsKitJobHandler(grpc, fixture.Delivery);
        var payload = new DocsKitJobPayload(["HOP_DONG", "MAU_THIEU"], null, null, null, null);
        var ctx = new JobContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), JsonSerializer.Serialize(payload), new NoopProgress());

        var result = await handler.RunAsync(ctx, CancellationToken.None);

        result.Summary.Should().Contain("1/2");
        result.Summary.Should().Contain("MAU_THIEU");
        result.Summary.Should().Contain("Bỏ qua");
    }

    [Fact]
    public async Task RunAsync_SingleTemplateInvalidArgument_ThrowsWithSkippedList()
    {
        var grpc = Substitute.For<Clawbot.Agents.Contracts.Docs.DocsAgent.DocsAgentClient>();
        grpc.GenerateAsync(Arg.Any<DocGenerateRequest>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(_ => throw new RpcException(new Status(StatusCode.InvalidArgument, "missing field")));

        await using var fixture = await DocsKitFixture.CreateAsync();
        var handler = new DocsKitJobHandler(grpc, fixture.Delivery);
        var payload = new DocsKitJobPayload(["MAU_THIEU"], null, null, null, null);
        var ctx = new JobContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), JsonSerializer.Serialize(payload), new NoopProgress());

        var act = async () => await handler.RunAsync(ctx, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Không sinh được tài liệu nào*")
            .WithMessage("*MAU_THIEU*");
    }

    [Fact]
    public async Task RunAsync_NullPayload_Throws()
    {
        var grpc = Substitute.For<Clawbot.Agents.Contracts.Docs.DocsAgent.DocsAgentClient>();
        await using var fixture = await DocsKitFixture.CreateAsync();
        var handler = new DocsKitJobHandler(grpc, fixture.Delivery);
        var ctx = new JobContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "null", new NoopProgress());

        var act = async () => await handler.RunAsync(ctx, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static AsyncUnaryCall<T> CompletedUnaryCall<T>(T response) where T : class =>
        new(Task.FromResult(response), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });

    private sealed class NoopProgress : IJobProgress
    {
        public Task ReportAsync(int percent, string? note, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class DocsKitFixture(SqliteConnection connection, AppDbContext db, DocumentDeliveryService delivery) : IAsyncDisposable
    {
        public DocumentDeliveryService Delivery { get; } = delivery;

        public static async Task<DocsKitFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options, new NullTenantAccessor());
            await db.Database.EnsureCreatedAsync();
            var delivery = new DocumentDeliveryService(
                db, Substitute.For<IEmailSender>(), Array.Empty<IChannelAdapter>(),
                Substitute.For<IDocumentStorage>(), new DocsStorageOptions(), Substitute.For<IClock>());
            return new DocsKitFixture(connection, db, delivery);
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
        public TenantContext Require() => throw new InvalidOperationException("No tenant in unit test scope.");
    }
}
