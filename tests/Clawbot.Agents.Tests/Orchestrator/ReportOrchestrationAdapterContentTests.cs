using System.Text.Json;
using Clawbot.AgentService.Services;
using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.Domain.Analytics;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Clawbot.Agents.Tests.Orchestrator;

/// <summary>
/// Đường đi từ input goal của Orchestrator tới báo cáo marketing: operation phải định tuyến đúng và
/// artifact chốt lại phải là bảng nội dung, không phải bảng KPI sale.
/// </summary>
public sealed class ReportOrchestrationAdapterContentTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 3, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("content_snapshot", "content_snapshot")]
    [InlineData("Content", "content_snapshot")]
    [InlineData("marketing", "content_snapshot")]
    [InlineData("engagement", "content_snapshot")]
    [InlineData("content funnel", "content_funnel")]
    [InlineData("pipeline", "content_funnel")]
    [InlineData("snapshot", "snapshot")]
    [InlineData("anomaly", "anomaly")]
    public void NormalizeOperation_MapsAliasesToCanonicalOperation(string raw, string expected) =>
        ReportOrchestrationAdapter.NormalizeOperation(raw, description: "").Should().Be(expected);

    [Fact]
    public void NormalizeOperation_InfersContentReport_WhenOperationMissingAndGoalIsAboutContent() =>
        ReportOrchestrationAdapter
            .NormalizeOperation(operation: null, "Báo cáo hiệu quả bài đăng tuần này")
            .Should().Be(ReportArtifact.KindContentSnapshot);

    [Fact]
    public void NormalizeOperation_KeepsSaleSnapshot_WhenGoalDoesNotMentionContent() =>
        ReportOrchestrationAdapter
            .NormalizeOperation(operation: null, "Báo cáo lead và tỉ lệ chuyển đổi hôm nay")
            .Should().Be("snapshot");

    [Fact]
    public async Task ExecuteAsync_ContentSnapshot_ReturnsPublishedPostsAndPersistsArtifact()
    {
        await using var fixture = await AdapterFixture.CreateAsync();
        fixture.AddPostedSchedule("facebook", Now.AddDays(-1), likes: 12, comments: 4);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Adapter.ExecuteAsync(fixture.Task(new()
        {
            ["operation"] = "content_snapshot",
            ["date"] = "2026-08-20",
        }), CancellationToken.None);

        result.Success.Should().BeTrue();
        using var payload = JsonDocument.Parse(result.Output);
        var root = payload.RootElement;
        root.GetProperty("operation").GetString().Should().Be(ReportArtifact.KindContentSnapshot);
        root.GetProperty("from").GetString().Should().Be("2026-08-14");
        root.GetProperty("to").GetString().Should().Be("2026-08-20");
        root.GetProperty("postsPublished").GetInt32().Should().Be(1);
        root.GetProperty("likes").GetInt32().Should().Be(12);
        root.GetProperty("comments").GetInt32().Should().Be(4);
        root.GetProperty("reportUrl").GetString().Should().StartWith("/reports/");

        var artifact = await fixture.Db.ReportArtifacts.IgnoreQueryFilters().SingleAsync();
        artifact.Kind.Should().Be(ReportArtifact.KindContentSnapshot);
        // Cột phải là số liệu nội dung — đây chính là chỗ trước đây trả về bảng lead của sale.
        artifact.DataJson.Should().Contain("postsPublished").And.Contain("reactionsTotal");
        artifact.DataJson.Should().NotContain("conversions");
    }

    [Fact]
    public async Task ExecuteAsync_ContentFunnel_ReturnsWorkflowStageCounts()
    {
        await using var fixture = await AdapterFixture.CreateAsync();
        fixture.AddItem("facebook");
        fixture.AddItem("facebook", item => item.Reject(Now, "sai brand voice"));
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Adapter.ExecuteAsync(fixture.Task(new()
        {
            ["operation"] = "content_funnel",
            ["date"] = "2026-08-20",
        }), CancellationToken.None);

        result.Success.Should().BeTrue();
        using var payload = JsonDocument.Parse(result.Output);
        var root = payload.RootElement;
        root.GetProperty("operation").GetString().Should().Be(ReportArtifact.KindContentFunnel);
        root.GetProperty("totalItems").GetInt32().Should().Be(2);
        root.GetProperty("truncated").GetBoolean().Should().BeFalse();
        // Không truyền lookback_days = ảnh chụp tồn đọng, không có mốc chặn dưới.
        root.GetProperty("from").ValueKind.Should().Be(JsonValueKind.Null);

        var row = root.GetProperty("items")[0];
        row.GetProperty("platform").GetString().Should().Be("facebook");
        row.GetProperty("awaitingAgentReview").GetInt32().Should().Be(1);
        row.GetProperty("rejected").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ContentSnapshot_SkipsArtifact_WhenNothingPublished()
    {
        await using var fixture = await AdapterFixture.CreateAsync();

        var result = await fixture.Adapter.ExecuteAsync(fixture.Task(new()
        {
            ["operation"] = "content_snapshot",
        }), CancellationToken.None);

        result.Success.Should().BeTrue();
        using var payload = JsonDocument.Parse(result.Output);
        payload.RootElement.GetProperty("platformCount").GetInt32().Should().Be(0);
        // Link dẫn tới bảng rỗng còn tệ hơn không có link.
        payload.RootElement.GetProperty("reportUrl").ValueKind.Should().Be(JsonValueKind.Null);
        (await fixture.Db.ReportArtifacts.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    private sealed class AdapterFixture(
        SqliteConnection connection,
        AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public Guid TenantId { get; } = Guid.NewGuid();
        public ReportOrchestrationAdapter Adapter { get; } = new(new ReportAgentRunner(
            db,
            Substitute.For<IAnomalyDetector>(),
            Substitute.For<IForecaster>()));

        public static async Task<AdapterFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AppDbContext(options, new NullTenantAccessor());
            var createScript = db.Database.GenerateCreateScript()
                .Replace("nvarchar(max)", "TEXT", StringComparison.OrdinalIgnoreCase)
                .Replace("varchar(max)", "TEXT", StringComparison.OrdinalIgnoreCase)
                .Replace("varbinary(max)", "BLOB", StringComparison.OrdinalIgnoreCase)
                .Replace("N'", "'", StringComparison.Ordinal);
            await db.Database.ExecuteSqlRawAsync(createScript);
            return new AdapterFixture(connection, db);
        }

        public AgentTask Task(Dictionary<string, string> input)
        {
            input["tenant_id"] = TenantId.ToString("D");
            return new AgentTask("task-1", "report-agent", "Báo cáo nội dung", input);
        }

        public void AddItem(string platform, Action<ContentItem>? mutate = null)
        {
            var item = ContentItem.Create(
                TenantId, platform, "Nội dung nháp", createdBy: null, Now.AddDays(-1));
            mutate?.Invoke(item);
            Db.ContentItems.Add(item);
        }

        public void AddPostedSchedule(string platform, DateTimeOffset postedAt, int likes, int comments)
        {
            var schedule = ContentSchedule.Schedule(
                TenantId, Guid.NewGuid(), contentRevision: 1, platform, postedAt, postedAt);
            schedule.MarkPublishing(postedAt);
            schedule.MarkPosted("https://facebook.com/post", externalPostId: null, postedAt);
            schedule.SetEngagement(likes, comments, postedAt);
            Db.ContentSchedules.Add(schedule);
        }

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
