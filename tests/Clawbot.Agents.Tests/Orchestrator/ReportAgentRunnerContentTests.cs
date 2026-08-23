using Clawbot.AgentService.Services;
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
/// Số liệu marketing của report-agent: bài đã đăng kèm tương tác và phễu duyệt nội dung.
/// Ba operation cũ (snapshot/anomaly/forecast) đọc kpi_daily nên chỉ có chỉ số sale — các test ở đây
/// khóa lại việc hai operation mới đọc đúng bảng nội dung.
/// </summary>
public sealed class ReportAgentRunnerContentTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ContentSnapshotAsync_AggregatesPostedSchedulesPerPlatform()
    {
        await using var fixture = await RunnerFixture.CreateAsync();
        fixture.AddPostedSchedule("facebook", Now.AddDays(-1), likes: 10, comments: 3, reactionsTotal: 14);
        fixture.AddPostedSchedule("facebook", Now.AddDays(-2), likes: 5, comments: 1, reactionsTotal: 6);
        fixture.AddPostedSchedule("instagram", Now.AddDays(-1), likes: 7, comments: 2, reactionsTotal: null);
        await fixture.Db.SaveChangesAsync();

        var rows = await fixture.Runner.ContentSnapshotAsync(
            fixture.TenantId,
            DateOnly.FromDateTime(Now.AddDays(-6).Date),
            DateOnly.FromDateTime(Now.Date),
            "all",
            CancellationToken.None);

        rows.Should().HaveCount(2);
        var facebook = rows.Single(r => r.Platform == "facebook");
        facebook.PostsPublished.Should().Be(2);
        facebook.Likes.Should().Be(15);
        facebook.Comments.Should().Be(4);
        facebook.ReactionsTotal.Should().Be(20);

        // Instagram không có phân loại reaction: tổng cảm xúc lấy theo lượt thích chứ không để trống.
        var instagram = rows.Single(r => r.Platform == "instagram");
        instagram.PostsPublished.Should().Be(1);
        instagram.ReactionsTotal.Should().Be(7);
    }

    [Fact]
    public async Task ContentSnapshotAsync_ExcludesSchedulesOutsideWindowOrNotPosted()
    {
        await using var fixture = await RunnerFixture.CreateAsync();
        fixture.AddPostedSchedule("facebook", Now.AddDays(-1), likes: 4, comments: 1, reactionsTotal: 4);
        fixture.AddPostedSchedule("facebook", Now.AddDays(-40), likes: 100, comments: 50, reactionsTotal: 100);
        fixture.AddPendingSchedule("facebook");
        await fixture.Db.SaveChangesAsync();

        var rows = await fixture.Runner.ContentSnapshotAsync(
            fixture.TenantId,
            DateOnly.FromDateTime(Now.AddDays(-6).Date),
            DateOnly.FromDateTime(Now.Date),
            "all",
            CancellationToken.None);

        rows.Should().ContainSingle();
        rows[0].PostsPublished.Should().Be(1);
        rows[0].Likes.Should().Be(4);
    }

    [Fact]
    public async Task ContentSnapshotAsync_FiltersByPlatform()
    {
        await using var fixture = await RunnerFixture.CreateAsync();
        fixture.AddPostedSchedule("facebook", Now.AddDays(-1), likes: 10, comments: 3, reactionsTotal: 10);
        fixture.AddPostedSchedule("instagram", Now.AddDays(-1), likes: 7, comments: 2, reactionsTotal: 7);
        await fixture.Db.SaveChangesAsync();

        var rows = await fixture.Runner.ContentSnapshotAsync(
            fixture.TenantId,
            DateOnly.FromDateTime(Now.AddDays(-6).Date),
            DateOnly.FromDateTime(Now.Date),
            "Instagram",
            CancellationToken.None);

        rows.Should().ContainSingle();
        rows[0].Platform.Should().Be("instagram");
    }

    [Fact]
    public async Task ContentSnapshotAsync_IgnoresOtherTenants()
    {
        await using var fixture = await RunnerFixture.CreateAsync();
        fixture.AddPostedSchedule("facebook", Now.AddDays(-1), likes: 9, comments: 1, reactionsTotal: 9);
        fixture.AddPostedSchedule(
            "facebook", Now.AddDays(-1), likes: 999, comments: 999, reactionsTotal: 999, tenantId: Guid.NewGuid());
        await fixture.Db.SaveChangesAsync();

        var rows = await fixture.Runner.ContentSnapshotAsync(
            fixture.TenantId,
            DateOnly.FromDateTime(Now.AddDays(-6).Date),
            DateOnly.FromDateTime(Now.Date),
            "all",
            CancellationToken.None);

        rows.Should().ContainSingle();
        rows[0].Likes.Should().Be(9);
    }

    [Fact]
    public async Task ContentFunnelAsync_CountsItemsByWorkflowState()
    {
        await using var fixture = await RunnerFixture.CreateAsync();
        fixture.AddItem("facebook");                                  // awaiting_agent_review
        fixture.AddItem("facebook", item => item.BeginAgentReview(item.ContentRevision, Now)); // agent_review_running
        fixture.AddItem("facebook", MarkReviewPassed);                // awaiting_human_approval
        fixture.AddItem("facebook", item => item.Reject(Now, "sai brand voice")); // rejected
        fixture.AddItem("instagram");
        await fixture.Db.SaveChangesAsync();

        var report = await fixture.Runner.ContentFunnelAsync(
            fixture.TenantId,
            fromDate: null,
            DateOnly.FromDateTime(Now.Date),
            "all",
            CancellationToken.None);

        report.Truncated.Should().BeFalse();
        report.Rows.Should().HaveCount(2);
        var facebook = report.Rows.Single(r => r.Platform == "facebook");
        facebook.Total.Should().Be(4);
        facebook.AwaitingAgentReview.Should().Be(1);
        facebook.AgentReviewRunning.Should().Be(1);
        facebook.AwaitingHumanApproval.Should().Be(1);
        facebook.Rejected.Should().Be(1);
        facebook.Published.Should().Be(0);

        report.Rows.Single(r => r.Platform == "instagram").Total.Should().Be(1);
    }

    [Fact]
    public async Task ContentFunnelAsync_ExcludesDeletedItems()
    {
        await using var fixture = await RunnerFixture.CreateAsync();
        fixture.AddItem("facebook");
        fixture.AddItem("facebook", item => item.SoftDelete(Now));
        await fixture.Db.SaveChangesAsync();

        var report = await fixture.Runner.ContentFunnelAsync(
            fixture.TenantId,
            fromDate: null,
            DateOnly.FromDateTime(Now.Date),
            "all",
            CancellationToken.None);

        report.Rows.Should().ContainSingle();
        report.Rows[0].Total.Should().Be(1);
        report.Rows[0].AwaitingAgentReview.Should().Be(1);
    }

    /// <summary>
    /// Bài kẹt lâu nhất là thứ đáng báo cáo nhất. Nếu phễu lọc theo ngày tạo thì chính những bài đó
    /// biến mất — đây là hồi quy dễ tái diễn nhất khi ai đó "chuẩn hóa" phễu về cùng cửa sổ với snapshot.
    /// </summary>
    [Fact]
    public async Task ContentFunnelAsync_KeepsLongStuckItems_WhenNoLookbackRequested()
    {
        await using var fixture = await RunnerFixture.CreateAsync();
        fixture.AddItem("facebook", createdAt: Now.AddDays(-120));
        fixture.AddItem("facebook", createdAt: Now.AddDays(-1));
        await fixture.Db.SaveChangesAsync();

        var report = await fixture.Runner.ContentFunnelAsync(
            fixture.TenantId,
            fromDate: null,
            DateOnly.FromDateTime(Now.Date),
            "all",
            CancellationToken.None);

        report.Rows.Single().Total.Should().Be(2);
    }

    [Fact]
    public async Task ContentFunnelAsync_AppliesLowerBound_WhenLookbackRequested()
    {
        await using var fixture = await RunnerFixture.CreateAsync();
        fixture.AddItem("facebook", createdAt: Now.AddDays(-120));
        fixture.AddItem("facebook", createdAt: Now.AddDays(-1));
        await fixture.Db.SaveChangesAsync();

        var report = await fixture.Runner.ContentFunnelAsync(
            fixture.TenantId,
            DateOnly.FromDateTime(Now.AddDays(-6).Date),
            DateOnly.FromDateTime(Now.Date),
            "all",
            CancellationToken.None);

        report.Rows.Single().Total.Should().Be(1);
    }

    [Theory]
    [InlineData(null, 7, "2026-08-14", "2026-08-20")]
    [InlineData(30, 7, "2026-07-22", "2026-08-20")]
    [InlineData(0, 30, "2026-07-22", "2026-08-20")]
    public void ResolveRange_BuildsInclusiveWindowEndingOnRequestedDate(
        int? days, int defaultDays, string expectedFrom, string expectedTo)
    {
        var (from, to) = ReportAgentRunner.ResolveRange("2026-08-20", days, defaultDays);

        ReportAgentRunner.FormatDate(from).Should().Be(expectedFrom);
        ReportAgentRunner.FormatDate(to).Should().Be(expectedTo);
    }

    [Fact]
    public void ResolveRange_CapsWindowAtOneYear()
    {
        var (from, to) = ReportAgentRunner.ResolveRange("2026-08-20", 5000, 7);

        to.DayNumber.Should().Be(from.DayNumber + 364);
    }

    [Fact]
    public void ReportArtifactKinds_CoverBothMarketingReports()
    {
        ReportArtifact.KindContentSnapshot.Should().Be("content_snapshot");
        ReportArtifact.KindContentFunnel.Should().Be("content_funnel");
    }

    private static void MarkReviewPassed(ContentItem item)
    {
        item.BeginAgentReview(item.ContentRevision, Now);
        item.RecordAgentReview(
            item.ContentRevision,
            ContentItem.ReviewStatusPassed,
            ContentItem.ImageReviewStatusNotApplicable,
            reviewedImageCount: 0,
            Guid.NewGuid(),
            reason: null,
            Now);
    }

    private sealed class RunnerFixture(
        SqliteConnection connection,
        AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public Guid TenantId { get; } = Guid.NewGuid();
        public ReportAgentRunner Runner { get; } = new(
            db,
            Substitute.For<IAnomalyDetector>(),
            Substitute.For<IForecaster>());

        public static async Task<RunnerFixture> CreateAsync()
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
            return new RunnerFixture(connection, db);
        }

        public void AddItem(
            string platform,
            Action<ContentItem>? mutate = null,
            DateTimeOffset? createdAt = null)
        {
            var item = ContentItem.Create(
                TenantId,
                platform,
                "Nội dung nháp",
                createdBy: null,
                createdAt ?? Now.AddDays(-1));
            mutate?.Invoke(item);
            Db.ContentItems.Add(item);
        }

        public void AddPostedSchedule(
            string platform,
            DateTimeOffset postedAt,
            int likes,
            int comments,
            int? reactionsTotal,
            Guid? tenantId = null)
        {
            var schedule = ContentSchedule.Schedule(
                tenantId ?? TenantId,
                Guid.NewGuid(),
                contentRevision: 1,
                platform,
                postedAt,
                postedAt);
            schedule.MarkPublishing(postedAt);
            schedule.MarkPosted("https://facebook.com/post", externalPostId: null, postedAt);
            schedule.SetFacebookEngagement(
                likes, comments, reactionsTotal, love: null, haha: null, wow: null,
                sad: null, angry: null, care: null, postedAt);
            Db.ContentSchedules.Add(schedule);
        }

        public void AddPendingSchedule(string platform)
        {
            Db.ContentSchedules.Add(ContentSchedule.Schedule(
                TenantId,
                Guid.NewGuid(),
                contentRevision: 1,
                platform,
                Now.AddDays(1),
                Now));
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
