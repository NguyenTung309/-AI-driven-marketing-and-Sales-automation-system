using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Rag;
using Clawbot.Api.Services;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Clawbot.Api.Tests.Integration;

// Unit test thuần cho KbTestingOrchestrator (internal sealed class, gọi trực tiếp được nhờ
// InternalsVisibleTo Clawbot.Api.Tests trên Clawbot.Api.csproj). ScaleCaseCount là static/pure nên
// test không cần DB; GenerateAndSaveAsync/RunAndRecordAsync cần AppDbContext thật trên SQLite in-memory
// (giống SaleAssistUpsellSuggestionServiceTests) vì orchestrator dùng EF.Functions/IgnoreQueryFilters.
public sealed class KbTestingOrchestratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);

    // ---------- ScaleCaseCount (static, pure) ----------

    [Theory]
    [InlineData(500, 40, 8)] // Nội dung ngắn -> kẹp sàn MinCases (8).
    [InlineData(100_000, 20, 20)] // Nội dung rất dài -> kẹp trần đúng maxCases truyền vào.
    [InlineData(10_000, 40, 10)] // Nội dung vừa phải -> đúng công thức ceil(length/1000).
    [InlineData(8_000, 40, 8)] // Biên đúng ceil(8000/1000)=8, trùng sàn nhưng không phải do kẹp.
    public void ScaleCaseCount_VariousContentLengths_ReturnsExpectedClampedCount(
        int contentLength, int maxCases, int expected)
    {
        var result = KbTestingOrchestrator.ScaleCaseCount(contentLength, maxCases);

        result.Should().Be(expected);
    }

    // ---------- GenerateAndSaveAsync ----------

    [Fact]
    public async Task GenerateAndSaveAsync_ModuleWithoutAnyVersion_ReturnsNull()
    {
        await using var fixture = await KbOrchestratorFixture.CreateAsync();
        var module = await fixture.CreateModuleAsync();
        var orchestrator = fixture.BuildOrchestrator();

        var result = await orchestrator.GenerateAndSaveAsync(
            fixture.TenantId, module.Id, null, KbTestingOrchestrator.AutoUploadMaxCases, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GenerateAndSaveAsync_GeneratedQuestionAlreadyExists_SkipsDuplicateCaseInsensitively()
    {
        await using var fixture = await KbOrchestratorFixture.CreateAsync();
        var module = await fixture.CreateModuleAsync();
        await fixture.CreateVersionAsync(module.Id, 1, "nội dung tài liệu để sinh câu hỏi kiểm thử");
        await fixture.CreateTestCaseAsync(module.Id, "Giá gói là bao nhiêu?", "500k");

        var claude = Substitute.For<IClaudeChatClient>();
        // Câu đầu trùng (khác hoa/thường) với case đã có -> phải bị bỏ; câu sau mới -> được thêm.
        const string generatedJson =
            "[{\"question\":\"giá gói là bao nhiêu?\",\"expectedAnswer\":\"500k\"}," +
            "{\"question\":\"Bảo hành mấy tháng?\",\"expectedAnswer\":\"12 tháng\"}]";
        claude.CompleteAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply(generatedJson, 5, 5, 0m));
        var orchestrator = fixture.BuildOrchestrator(claude: claude);

        var result = await orchestrator.GenerateAndSaveAsync(
            fixture.TenantId, module.Id, 2, KbTestingOrchestrator.AutoUploadMaxCases, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Generated.Should().Be(2);
        result.Added.Should().Be(1);
        fixture.Db.ChangeTracker.Clear();
        var allCases = await fixture.Db.KbTestCases.IgnoreQueryFilters()
            .Where(t => t.KbModuleId == module.Id).ToListAsync();
        allCases.Should().HaveCount(2);
    }

    // ---------- RunAndRecordAsync ----------

    [Fact]
    public async Task RunAndRecordAsync_ModuleWithoutDeployedVersion_ReturnsNull()
    {
        await using var fixture = await KbOrchestratorFixture.CreateAsync();
        var module = await fixture.CreateModuleAsync();
        await fixture.CreateVersionAsync(module.Id, 1, "bản nháp chưa triển khai");
        var orchestrator = fixture.BuildOrchestrator();

        var result = await orchestrator.RunAndRecordAsync(
            fixture.TenantId, module.Code, module.Id, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RunAndRecordAsync_DeployedVersionWithoutActiveTestCases_ReturnsNull()
    {
        await using var fixture = await KbOrchestratorFixture.CreateAsync();
        var module = await fixture.CreateModuleAsync();
        await fixture.CreateDeployedVersionAsync(module.Id, 1, "bản đã triển khai");
        var orchestrator = fixture.BuildOrchestrator();

        var result = await orchestrator.RunAndRecordAsync(
            fixture.TenantId, module.Code, module.Id, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RunAndRecordAsync_AllCasesHaveNoContext_ThrowsNoVectorDataMessage()
    {
        await using var fixture = await KbOrchestratorFixture.CreateAsync();
        var module = await fixture.CreateModuleAsync();
        await fixture.CreateDeployedVersionAsync(module.Id, 1, "bản đã triển khai");
        await fixture.CreateTestCaseAsync(module.Id, "Câu 1", "Trả lời 1");

        var rag = Substitute.For<IRagRetriever>();
        rag.RetrieveAsync(Arg.Any<RagRequest>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<RagChunk>());
        var orchestrator = fixture.BuildOrchestrator(rag: rag);

        var act = async () => await orchestrator.RunAndRecordAsync(
            fixture.TenantId, module.Code, module.Id, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage(KbTestingOrchestrator.NoVectorDataMessage);
    }

    [Fact]
    public async Task RunAndRecordAsync_MixOfPassAndFail_RecordsRoundedScoreOnLatestDeployedVersion()
    {
        await using var fixture = await KbOrchestratorFixture.CreateAsync();
        var module = await fixture.CreateModuleAsync();
        var deployed = await fixture.CreateDeployedVersionAsync(module.Id, 1, "bản đã triển khai");
        await fixture.CreateTestCaseAsync(module.Id, "Câu 1", "Trả lời 1");
        await fixture.CreateTestCaseAsync(module.Id, "Câu 2", "Trả lời 2");
        await fixture.CreateTestCaseAsync(module.Id, "Câu 3", "Trả lời 3");

        var rag = Substitute.For<IRagRetriever>();
        rag.RetrieveAsync(Arg.Any<RagRequest>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new RagChunk(deployed.Id.ToString(), module.Code, "ngữ cảnh liên quan", 0.9f) });
        var claude = Substitute.For<IClaudeChatClient>();
        // Luân phiên đạt/không đạt theo thứ tự câu hỏi: 2 đạt trên 3 -> 66.67%.
        claude.CompleteAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(),
                Arg.Is<string>(p => p.Contains("Câu 1", StringComparison.Ordinal)), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply("{\"passed\":true,\"reason\":\"ok\"}", 5, 5, 0m));
        claude.CompleteAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(),
                Arg.Is<string>(p => p.Contains("Câu 2", StringComparison.Ordinal)), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply("{\"passed\":true,\"reason\":\"ok\"}", 5, 5, 0m));
        claude.CompleteAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(),
                Arg.Is<string>(p => p.Contains("Câu 3", StringComparison.Ordinal)), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply("{\"passed\":false,\"reason\":\"sai\"}", 5, 5, 0m));
        var orchestrator = fixture.BuildOrchestrator(rag: rag, claude: claude);

        var result = await orchestrator.RunAndRecordAsync(
            fixture.TenantId, module.Code, module.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Passed.Should().Be(2);
        result.Total.Should().Be(3);
        result.Version.Should().Be(1);
        result.Score.Should().Be(66.67m);
        fixture.Db.ChangeTracker.Clear();
        var reloaded = await fixture.Db.KbVersions.IgnoreQueryFilters().FirstAsync(v => v.Id == deployed.Id);
        reloaded.AccuracyScore.Should().Be(66.67m);
    }

    private sealed class KbOrchestratorFixture(SqliteConnection connection, AppDbContext db, IClock clock) : IAsyncDisposable
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public AppDbContext Db { get; } = db;
        public IClock Clock { get; } = clock;

        public static async Task<KbOrchestratorFixture> CreateAsync()
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
            return new KbOrchestratorFixture(connection, db, clock);
        }

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

        public async Task<KbVersion> CreateDeployedVersionAsync(Guid moduleId, int version, string contentMd)
        {
            var kbVersion = KbVersion.Create(moduleId, version, contentMd, Now.AddDays(-3));
            kbVersion.Deploy(Now.AddDays(-1));
            Db.KbVersions.Add(kbVersion);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return kbVersion;
        }

        public async Task<KbTestCase> CreateTestCaseAsync(Guid moduleId, string question, string expectedAnswer)
        {
            var testCase = KbTestCase.Create(moduleId, question, expectedAnswer, Now.AddDays(-2));
            Db.KbTestCases.Add(testCase);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return testCase;
        }

        public KbTestingOrchestrator BuildOrchestrator(IRagRetriever? rag = null, IClaudeChatClient? claude = null) =>
            new(
                Db,
                new KbTestRunnerService(
                    rag ?? Substitute.For<IRagRetriever>(),
                    claude ?? Substitute.For<IClaudeChatClient>(),
                    Substitute.For<ILlmCallScope>()),
                Clock);

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
}
