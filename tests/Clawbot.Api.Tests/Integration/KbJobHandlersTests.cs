using System.Text.Json;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Kb;
using Clawbot.Agents.Core.Rag;
using Clawbot.Api.Jobs;
using Clawbot.Api.Services;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Clawbot.SharedKernel.Vectors;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Clawbot.Api.Tests.Integration;

// Unit test thuần (không qua ApiTestFactory/HTTP host) cho 3 job handler KB chạy qua Hangfire, không
// có route HTTP riêng: KbTestJobHandler (kb.test), KbDeployJobHandler (kb.deploy) và
// KbGenerateTestCasesJobHandler (kb.test-cases-generate). Dùng AppDbContext thật trên SQLite in-memory
// (giống SaleAssistUpsellSuggestionServiceTests) vì handler dùng EF.Functions/IgnoreQueryFilters trực
// tiếp trên DbContext. KbTestRunnerService/KbDeployService không có interface riêng nhưng phụ thuộc
// của chúng (IRagRetriever, IClaudeChatClient, ILlmCallScope, IEmbeddingProvider, IVectorStore) đều là
// interface -> mock trực tiếp các phụ thuộc đó rồi new lên instance thật, khỏi cần ForPartsOf.
public sealed class KbJobHandlersTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);

    // ---------- KbTestJobHandler ----------

    [Fact]
    public async Task KbTestJobHandler_ModuleNotFound_ThrowsWithModuleNotFoundMessage()
    {
        await using var fixture = await KbFixture.CreateAsync();
        var handler = new KbTestJobHandler(fixture.Db, KbJobHandlersTests.KbFixture.BuildTestRunner());
        var ctx = fixture.BuildContext(new KbTestJobPayload(Guid.NewGuid()));

        var act = async () => await handler.RunAsync(ctx, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("Không tìm thấy KB module.");
    }

    [Fact]
    public async Task KbTestJobHandler_ModuleWithoutDeployedVersion_ThrowsWithNoDeployedVersionMessage()
    {
        await using var fixture = await KbFixture.CreateAsync();
        var module = await fixture.CreateModuleAsync();
        // Chỉ có bản draft, chưa deploy -> vẫn phải ném lỗi dù có version.
        await fixture.CreateVersionAsync(module.Id, 1, "nội dung nháp");
        var handler = new KbTestJobHandler(fixture.Db, KbJobHandlersTests.KbFixture.BuildTestRunner());
        var ctx = fixture.BuildContext(new KbTestJobPayload(module.Id));

        var act = async () => await handler.RunAsync(ctx, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("Module chưa có phiên bản nào được triển khai.");
    }

    [Fact]
    public async Task KbTestJobHandler_DeployedVersionOnlyInactiveTestCases_ThrowsWithNoTestCaseMessage()
    {
        await using var fixture = await KbFixture.CreateAsync();
        var module = await fixture.CreateModuleAsync();
        await fixture.CreateDeployedVersionAsync(module.Id, 1, "nội dung");
        // Test case tồn tại nhưng bị vô hiệu -> vẫn coi như "chưa có test case nào".
        await fixture.CreateTestCaseAsync(module.Id, "Câu hỏi cũ", "Trả lời cũ", isActive: false);
        var handler = new KbTestJobHandler(fixture.Db, KbJobHandlersTests.KbFixture.BuildTestRunner());
        var ctx = fixture.BuildContext(new KbTestJobPayload(module.Id));

        var act = async () => await handler.RunAsync(ctx, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("Module chưa có test case nào.");
    }

    [Fact]
    public async Task KbTestJobHandler_AllCasesPass_RecordsAccuracyAndReturnsSummaryWithScore()
    {
        await using var fixture = await KbFixture.CreateAsync();
        var module = await fixture.CreateModuleAsync();
        var version = await fixture.CreateDeployedVersionAsync(module.Id, 1, "nội dung đã triển khai");
        await fixture.CreateTestCaseAsync(module.Id, "Câu 1", "Trả lời 1");
        await fixture.CreateTestCaseAsync(module.Id, "Câu 2", "Trả lời 2");

        var rag = Substitute.For<IRagRetriever>();
        rag.RetrieveAsync(Arg.Any<RagRequest>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new RagChunk(version.Id.ToString(), module.Code, "đoạn ngữ cảnh liên quan", 0.9f) });
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply("{\"passed\":true,\"reason\":\"đúng\"}", 10, 10, 0m));
        var runner = KbJobHandlersTests.KbFixture.BuildTestRunner(rag, claude);
        var handler = new KbTestJobHandler(fixture.Db, runner);
        var ctx = fixture.BuildContext(new KbTestJobPayload(module.Id));

        var result = await handler.RunAsync(ctx, CancellationToken.None);

        result.ResultLink.Should().Be($"/kb?module={module.Id}");
        result.Summary.Should().Contain("100").And.Contain("(2/2 case đạt)").And.Contain("v1");
        fixture.Db.ChangeTracker.Clear();
        var reloaded = await fixture.Db.KbVersions.IgnoreQueryFilters()
            .FirstAsync(v => v.Id == version.Id);
        reloaded.AccuracyScore.Should().Be(100m);
    }

    [Fact]
    public async Task KbTestJobHandler_AllCasesHaveNoContext_ThrowsNoVectorDataMessage()
    {
        await using var fixture = await KbFixture.CreateAsync();
        var module = await fixture.CreateModuleAsync();
        await fixture.CreateDeployedVersionAsync(module.Id, 1, "nội dung đã triển khai");
        await fixture.CreateTestCaseAsync(module.Id, "Câu 1", "Trả lời 1");

        var rag = Substitute.For<IRagRetriever>();
        rag.RetrieveAsync(Arg.Any<RagRequest>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<RagChunk>());
        var runner = KbJobHandlersTests.KbFixture.BuildTestRunner(rag);
        var handler = new KbTestJobHandler(fixture.Db, runner);
        var ctx = fixture.BuildContext(new KbTestJobPayload(module.Id));

        var act = async () => await handler.RunAsync(ctx, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage(KbTestingOrchestrator.NoVectorDataMessage);
    }

    // ---------- KbDeployJobHandler ----------

    [Fact]
    public async Task KbDeployJobHandler_ModuleNotFound_ThrowsWithModuleNotFoundMessage()
    {
        await using var fixture = await KbFixture.CreateAsync();
        var handler = new KbDeployJobHandler(fixture.Db, KbJobHandlersTests.KbFixture.BuildDeployService(), fixture.Clock);
        var ctx = fixture.BuildContext(new KbDeployJobPayload(Guid.NewGuid(), Guid.NewGuid()));

        var act = async () => await handler.RunAsync(ctx, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("Không tìm thấy KB module.");
    }

    [Fact]
    public async Task KbDeployJobHandler_VersionNotFound_ThrowsWithVersionNotFoundMessage()
    {
        await using var fixture = await KbFixture.CreateAsync();
        var module = await fixture.CreateModuleAsync();
        var handler = new KbDeployJobHandler(fixture.Db, KbJobHandlersTests.KbFixture.BuildDeployService(), fixture.Clock);
        // Đúng module nhưng VersionId không tồn tại.
        var ctx = fixture.BuildContext(new KbDeployJobPayload(module.Id, Guid.NewGuid()));

        var act = async () => await handler.RunAsync(ctx, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("Không tìm thấy phiên bản KB.");
    }

    [Fact]
    public async Task KbDeployJobHandler_ValidDraftVersion_DeploysTargetAndArchivesPreviousDeployed()
    {
        await using var fixture = await KbFixture.CreateAsync();
        var module = await fixture.CreateModuleAsync();
        var previous = await fixture.CreateDeployedVersionAsync(module.Id, 1, "bản cũ", Now.AddDays(-5));
        var target = await fixture.CreateVersionAsync(module.Id, 2, "bản mới");

        var deployService = KbJobHandlersTests.KbFixture.BuildDeployService();
        var handler = new KbDeployJobHandler(fixture.Db, deployService, fixture.Clock);
        var ctx = fixture.BuildContext(new KbDeployJobPayload(module.Id, target.Id));

        var result = await handler.RunAsync(ctx, CancellationToken.None);

        result.ResultLink.Should().Be($"/kb?module={module.Id}");
        result.Summary.Should().Contain("Đã phát hành").And.Contain(module.Code).And.Contain("v2");
        fixture.Db.ChangeTracker.Clear();
        var reloadedTarget = await fixture.Db.KbVersions.IgnoreQueryFilters().FirstAsync(v => v.Id == target.Id);
        reloadedTarget.Status.Should().Be("deployed");
        reloadedTarget.DeployedAt.Should().Be(Now);
        var reloadedPrevious = await fixture.Db.KbVersions.IgnoreQueryFilters().FirstAsync(v => v.Id == previous.Id);
        reloadedPrevious.Status.Should().Be("archived");
    }

    [Fact]
    public async Task KbDeployJobHandler_RollbackPayload_ReturnsSummaryWithRollbackWording()
    {
        await using var fixture = await KbFixture.CreateAsync();
        var module = await fixture.CreateModuleAsync();
        var target = await fixture.CreateVersionAsync(module.Id, 1, "bản để khôi phục");

        var handler = new KbDeployJobHandler(fixture.Db, KbJobHandlersTests.KbFixture.BuildDeployService(), fixture.Clock);
        var ctx = fixture.BuildContext(new KbDeployJobPayload(module.Id, target.Id, IsRollback: true));

        var result = await handler.RunAsync(ctx, CancellationToken.None);

        result.Summary.Should().Contain("Đã khôi phục");
    }

    // ---------- KbGenerateTestCasesJobHandler ----------

    [Fact]
    public async Task KbGenerateTestCasesJobHandler_ModuleWithoutAnyVersion_ThrowsWithNoContentMessage()
    {
        await using var fixture = await KbFixture.CreateAsync();
        var module = await fixture.CreateModuleAsync();
        var orchestrator = fixture.BuildOrchestrator();
        var handler = new KbGenerateTestCasesJobHandler(orchestrator);
        var ctx = fixture.BuildContext(new KbGenerateTestCasesJobPayload(module.Id, null));

        var act = async () => await handler.RunAsync(ctx, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("Module chưa có nội dung để sinh test case.");
    }

    [Fact]
    public async Task KbGenerateTestCasesJobHandler_RunnerGeneratesZeroCases_ThrowsWithAgentFailedMessage()
    {
        await using var fixture = await KbFixture.CreateAsync();
        var module = await fixture.CreateModuleAsync();
        await fixture.CreateVersionAsync(module.Id, 1, "nội dung tài liệu dùng để sinh câu hỏi");

        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply("[]", 5, 5, 0m));
        var orchestrator = fixture.BuildOrchestrator(claude: claude);
        var handler = new KbGenerateTestCasesJobHandler(orchestrator);
        var ctx = fixture.BuildContext(new KbGenerateTestCasesJobPayload(module.Id, 2));

        var act = async () => await handler.RunAsync(ctx, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("Agent không sinh được test case nào.");
    }

    [Fact]
    public async Task KbGenerateTestCasesJobHandler_RunnerGeneratesNewCases_SavesAndReturnsAddedCountSummary()
    {
        await using var fixture = await KbFixture.CreateAsync();
        var module = await fixture.CreateModuleAsync();
        await fixture.CreateVersionAsync(module.Id, 1, "nội dung tài liệu dùng để sinh câu hỏi");

        var claude = Substitute.For<IClaudeChatClient>();
        const string generatedJson =
            "[{\"question\":\"Giá gói là bao nhiêu?\",\"expectedAnswer\":\"500k\"}," +
            "{\"question\":\"Bảo hành mấy tháng?\",\"expectedAnswer\":\"12 tháng\"}]";
        claude.CompleteAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply(generatedJson, 5, 5, 0m));
        var orchestrator = fixture.BuildOrchestrator(claude: claude);
        var handler = new KbGenerateTestCasesJobHandler(orchestrator);
        var ctx = fixture.BuildContext(new KbGenerateTestCasesJobPayload(module.Id, 2));

        var result = await handler.RunAsync(ctx, CancellationToken.None);

        result.ResultLink.Should().Be($"/kb?module={module.Id}");
        result.Summary.Should().Contain("Đã sinh 2 test case mới").And.Contain("bỏ 0 câu trùng");
        fixture.Db.ChangeTracker.Clear();
        var savedCases = await fixture.Db.KbTestCases.IgnoreQueryFilters()
            .Where(t => t.KbModuleId == module.Id).ToListAsync();
        savedCases.Should().HaveCount(2);
    }

    private sealed class KbFixture(SqliteConnection connection, AppDbContext db, IClock clock) : IAsyncDisposable
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public AppDbContext Db { get; } = db;
        public IClock Clock { get; } = clock;

        public static async Task<KbFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AppDbContext(options, new NullTenantAccessor());
            await db.Database.EnsureCreatedAsync();
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(Now);
            return new KbFixture(connection, db, clock);
        }

        public JobContext BuildContext(object payload) =>
            new(Guid.NewGuid(), TenantId, Guid.NewGuid(), JsonSerializer.Serialize(payload), new NoopJobProgress());

        public async Task<KbModule> CreateModuleAsync(string? code = null, string? name = null)
        {
            var module = KbModule.Create(TenantId, code ?? $"module-{Guid.NewGuid():N}", name ?? "Module test", Now.AddDays(-10));
            Db.KbModules.Add(module);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return module;
        }

        public async Task<KbVersion> CreateVersionAsync(Guid moduleId, int version, string contentMd)
        {
            var kbVersion = KbVersion.Create(moduleId, version, contentMd, Now.AddDays(-3));
            Db.KbVersions.Add(kbVersion);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return kbVersion;
        }

        public async Task<KbVersion> CreateDeployedVersionAsync(
            Guid moduleId, int version, string contentMd, DateTimeOffset? deployedAt = null)
        {
            var kbVersion = KbVersion.Create(moduleId, version, contentMd, Now.AddDays(-3));
            kbVersion.Deploy(deployedAt ?? Now.AddDays(-1));
            Db.KbVersions.Add(kbVersion);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return kbVersion;
        }

        public async Task<KbTestCase> CreateTestCaseAsync(
            Guid moduleId, string question, string expectedAnswer, bool isActive = true)
        {
            var testCase = KbTestCase.Create(moduleId, question, expectedAnswer, Now.AddDays(-2));
            Db.KbTestCases.Add(testCase);
            if (!isActive)
                Db.Entry(testCase).Property(nameof(KbTestCase.IsActive)).CurrentValue = false;
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return testCase;
        }

        // Không đụng field instance nào (Db/TenantId/Clock) -> đánh dấu static để tránh CA1822.
        public static KbTestRunnerService BuildTestRunner(IRagRetriever? rag = null, IClaudeChatClient? claude = null) =>
            new(
                rag ?? Substitute.For<IRagRetriever>(),
                claude ?? Substitute.For<IClaudeChatClient>(),
                Substitute.For<ILlmCallScope>());

        public static KbDeployService BuildDeployService(IEmbeddingProvider? embedder = null, IVectorStore? store = null)
        {
            var resolvedEmbedder = embedder ?? Substitute.For<IEmbeddingProvider>();
            resolvedEmbedder.Dimension.Returns(3);
            resolvedEmbedder.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new ReadOnlyMemory<float>(new[] { 0.1f, 0.2f, 0.3f }));
            return new KbDeployService(
                resolvedEmbedder,
                store ?? Substitute.For<IVectorStore>(),
                Substitute.For<ILogger<KbDeployService>>());
        }

        public KbTestingOrchestrator BuildOrchestrator(
            IRagRetriever? rag = null, IClaudeChatClient? claude = null) =>
            new(Db, BuildTestRunner(rag, claude), Clock);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public TenantContext? Current => null;

        public TenantContext Require() =>
            throw new InvalidOperationException("No tenant in unit test scope.");
    }

    private sealed class NoopJobProgress : IJobProgress
    {
        public Task ReportAsync(int percent, string? note, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
