using System.Globalization;
using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Domain.Analytics;
using Clawbot.Infrastructure.Leads;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.AgentService.Services;

/// <summary>
/// Orchestration adapter for the lead agent.
/// Operations: list/find_cold, score, create, batch_score.
/// </summary>
public sealed class LeadOrchestrationAdapter(
    LeadAgentRunner runner,
    LeadBatchRescorer batchRescorer,
    AppDbContext db) : AgentAdapterBase("lead-agent")
{
    private const int DefaultListLimit = 20;
    private const int MaxListLimit = 100;

    private readonly LeadAgentRunner _runner = runner;
    private readonly LeadBatchRescorer _batchRescorer = batchRescorer;
    private readonly AppDbContext _db = db;

    protected override async Task<string> ExecuteCoreAsync(AgentTask task, CancellationToken ct)
    {
        var input = task.Input;
        var operation = (AgentTaskInput.OptionalString(input, "operation")
            ?? InferOperation(input, task.Description)).ToLowerInvariant();

        if (operation is "list" or "find_cold" or "query" or "find")
            return await ListLeadsAsync(input, task.Description, ct).ConfigureAwait(false);

        if (operation is "create")
        {
            var result = await _runner.CreateWithSkillsAsync(new LeadCreateInput(
                AgentTaskInput.RequiredGuid(input, "tenant_id"),
                AgentTaskInput.RequiredGuid(input, "contact_id"),
                AgentTaskInput.RequiredString(input, "source_platform"),
                AgentTaskInput.OptionalString(input, "display_name"),
                AgentTaskInput.OptionalString(input, "phone"),
                AgentTaskInput.OptionalString(input, "email"),
                AgentTaskInput.OptionalString(input, "locale"),
                AgentTaskInput.OptionalString(input, "country"),
                AgentTaskInput.OptionalString(input, "note")), ct).ConfigureAwait(false);
            return Json(result);
        }

        if (operation is "batch_score" or "rescore" or "score_all" or "prioritize")
        {
            var tenantId = AgentTaskInput.RequiredGuid(input, "tenant_id");
            var topN = OptionalInt(input, "topN")
                ?? OptionalInt(input, "top_n")
                ?? 5;
            if (topN > 50) topN = 5;
            var batch = await _batchRescorer.RescoreTenantAsync(tenantId, topN, ct).ConfigureAwait(false);
            return Json(batch);
        }

        // Single-lead score: requires lead_id. Do not silently batch-rescore list goals.
        if (AgentTaskInput.OptionalGuid(input, "lead_id") is null)
            throw new ArgumentException(
                "lead_id required for score. Use operation=list to query cold/warm leads, or operation=batch_score to rescore.");

        var features = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (AgentTaskInput.OptionalString(input, "event_code") is { } eventCode)
            features["event_code"] = eventCode;
        if (AgentTaskInput.OptionalString(input, "platform") is { } platform)
            features["platform"] = platform;

        var score = await _runner.ScoreAsync(
            AgentTaskInput.RequiredGuid(input, "tenant_id"),
            AgentTaskInput.RequiredGuid(input, "lead_id"),
            features, ct).ConfigureAwait(false);
        return Json(score);
    }

    private async Task<string> ListLeadsAsync(
        IReadOnlyDictionary<string, string> input,
        string description,
        CancellationToken ct)
    {
        var tenantId = AgentTaskInput.RequiredGuid(input, "tenant_id");
        var limit = OptionalInt(input, "topN")
            ?? OptionalInt(input, "top_n")
            ?? OptionalInt(input, "limit")
            ?? DefaultListLimit;
        if (limit < 1) limit = DefaultListLimit;
        if (limit > MaxListLimit) limit = MaxListLimit;

        var stageFilter = NormalizeStageFilter(
            AgentTaskInput.OptionalString(input, "stage")
            ?? InferStageFromDescription(description));

        var inactiveDays = OptionalInt(input, "inactive_days")
            ?? OptionalInt(input, "inactiveDays");
        DateTimeOffset? inactiveBefore = inactiveDays is > 0
            ? DateTimeOffset.UtcNow.AddDays(-inactiveDays.Value)
            : null;

        var query = _db.Leads.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.DeletedAt == null);

        if (stageFilter is { Count: > 0 })
            query = query.Where(l => stageFilter.Contains(l.Stage));

        if (inactiveBefore is { } before)
            query = query.Where(l => (l.LastActivityAt ?? l.CreatedAt) < before);

        // SQLite test provider cannot ORDER BY DateTimeOffset; order by Score in SQL then stage/recency in-memory.
        // Cap over-fetch so cold/warm still surface when mixed with hot/customer.
        var raw = await query
            .OrderByDescending(l => l.Score)
            .Take(Math.Min(MaxListLimit, Math.Max(limit * 5, 50)))
            .Select(l => new
            {
                l.Id,
                l.ContactId,
                l.OwnerUserId,
                l.Score,
                l.Stage,
                l.SourcePlatform,
                l.LastActivityAt,
                l.CreatedAt,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var leads = raw
            .OrderBy(l => l.Stage == "cold" ? 0 : l.Stage == "warm" ? 1 : l.Stage == "hot" ? 2 : 3)
            .ThenBy(l => l.LastActivityAt ?? l.CreatedAt)
            .ThenByDescending(l => l.Score)
            .Take(limit)
            .ToList();

        var contactIds = leads.Where(l => l.ContactId is not null).Select(l => l.ContactId!.Value).Distinct().ToList();
        var names = contactIds.Count == 0
            ? new Dictionary<Guid, string?>()
            : await _db.Contacts.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.TenantId == tenantId && contactIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => (string?)c.DisplayName, ct)
                .ConfigureAwait(false);

        var items = leads.Select(l => new
        {
            lead_id = l.Id,
            contact_id = l.ContactId,
            owner_user_id = l.OwnerUserId,
            score = l.Score,
            stage = l.Stage,
            source_platform = l.SourcePlatform,
            last_activity_at = l.LastActivityAt,
            created_at = l.CreatedAt,
            contact_name = l.ContactId is { } cid && names.TryGetValue(cid, out var n) ? n : null,
        }).ToList();

        var leadIds = items.Select(i => i.lead_id.ToString("D")).ToArray();

        return Json(new
        {
            operation = "list",
            total = items.Count,
            stages = stageFilter,
            inactive_days = inactiveDays,
            lead_ids = leadIds,
            items,
        });
    }

    private static List<string>? NormalizeStageFilter(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ["cold", "warm", "hot"];

        var stages = raw.Split([',', '|', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .Where(s => s is "cold" or "warm" or "hot" or "customer" or "lost")
            .Distinct()
            .ToList();
        return stages.Count == 0 ? ["cold", "warm", "hot"] : stages;
    }

    private static string? InferStageFromDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        var d = description.ToLowerInvariant();
        if (d.Contains("cold", StringComparison.Ordinal) || d.Contains("lạnh", StringComparison.Ordinal) || d.Contains("lanh", StringComparison.Ordinal))
            return "cold";
        if (d.Contains("warm", StringComparison.Ordinal) || d.Contains("ấm", StringComparison.Ordinal))
            return "warm";
        if (d.Contains("hot", StringComparison.Ordinal) || d.Contains("nóng", StringComparison.Ordinal))
            return "hot";
        return null;
    }

    private static string InferOperation(IReadOnlyDictionary<string, string> input, string description)
    {
        if (input.ContainsKey("leadCount") || input.ContainsKey("criteria") || input.ContainsKey("topN") || input.ContainsKey("top_n"))
        {
            if (LooksLikeList(description))
                return "list";
            return "batch_score";
        }

        if (LooksLikeList(description))
            return "list";

        if (AgentTaskInput.OptionalGuid(input, "lead_id") is null)
            return "list";

        return "score";
    }

    private static bool LooksLikeList(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return false;
        var d = description.ToLowerInvariant();
        return d.Contains("list", StringComparison.Ordinal)
            || d.Contains("find", StringComparison.Ordinal)
            || d.Contains("xác định", StringComparison.Ordinal)
            || d.Contains("xac dinh", StringComparison.Ordinal)
            || d.Contains("cold", StringComparison.Ordinal)
            || d.Contains("lạnh", StringComparison.Ordinal)
            || d.Contains("lanh", StringComparison.Ordinal)
            || d.Contains("query", StringComparison.Ordinal)
            || d.Contains("danh sách", StringComparison.Ordinal)
            || d.Contains("danh sach", StringComparison.Ordinal)
            || d.Contains("ưu tiên", StringComparison.Ordinal)
            || d.Contains("uu tien", StringComparison.Ordinal);
    }

    private static int? OptionalInt(IReadOnlyDictionary<string, string> input, string key) =>
        input.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}

/// <summary>
/// Orchestration adapter for the report agent.
/// </summary>
public sealed class ReportOrchestrationAdapter(ReportAgentRunner runner) : AgentAdapterBase("report-agent")
{
    private readonly ReportAgentRunner _runner = runner;

    protected override async Task<string> ExecuteCoreAsync(AgentTask task, CancellationToken ct)
    {
        var input = task.Input;
        var operation = NormalizeOperation(
            AgentTaskInput.OptionalString(input, "operation"),
            task.Description);
        var tenantId = AgentTaskInput.RequiredGuid(input, "tenant_id");

        // platform mặc định "all" (đúng cách LoadSeriesAsync đã chuẩn hóa): bắt buộc nó chỉ khiến ReAct loop
        // đốt một bước cho lỗi "platform required." trong khi tổng hợp toàn nền tảng mới là mặc định hợp lý.
        var platform = AgentTaskInput.OptionalString(input, "platform") ?? "all";

        if (operation == ReportArtifact.KindContentSnapshot)
            return await ContentSnapshotAsync(tenantId, platform, input, ct).ConfigureAwait(false);

        if (operation == ReportArtifact.KindContentFunnel)
            return await ContentFunnelAsync(tenantId, platform, input, ct).ConfigureAwait(false);

        if (operation == "anomaly")
        {
            var metric = AgentTaskInput.RequiredString(input, "metric");
            var points = await _runner.DetectAnomalyAsync(
                tenantId,
                platform,
                metric,
                OptionalDouble(input, "z_threshold") ?? 0d,
                OptionalInt(input, "lookback_days") ?? 0, ct).ConfigureAwait(false);

            var items = points.Select(p => Row(
                ("date", ReportAgentRunner.FormatDate(p.At)),
                ("value", p.Value),
                ("zScore", p.ZScore),
                ("isAnomaly", p.IsAnomaly))).ToList();

            var link = await PersistAsync(
                tenantId,
                ReportArtifact.KindAnomaly,
                $"Bất thường {metric} ({platform})",
                platform,
                metric,
                points.Count > 0 ? DateOnly.FromDateTime(points[0].At.Date) : ReportAgentRunner.Today(),
                points.Count > 0 ? DateOnly.FromDateTime(points[^1].At.Date) : ReportAgentRunner.Today(),
                new ReportArtifactPayload(
                    ReportArtifact.KindAnomaly,
                    [
                        new ReportColumn("date", "Ngày", "date"),
                        new ReportColumn("value", "Giá trị", "number"),
                        new ReportColumn("zScore", "Z-score", "number"),
                        new ReportColumn("isAnomaly", "Bất thường", "text"),
                    ],
                    items,
                    new ReportChart("date", ["value"])),
                ct).ConfigureAwait(false);

            return Json(new
            {
                operation = "anomaly",
                metric,
                platform,
                total = items.Count,
                anomalies = points.Count(p => p.IsAnomaly),
                items,
                reportId = link?.Id,
                reportUrl = link?.Url,
            });
        }

        if (operation == "forecast")
        {
            var metric = AgentTaskInput.RequiredString(input, "metric");
            var points = await _runner.ForecastAsync(
                tenantId,
                platform,
                metric,
                OptionalInt(input, "horizon_days") ?? 0, ct).ConfigureAwait(false);

            var items = points.Select(p => Row(
                ("date", ReportAgentRunner.FormatDate(p.At)),
                ("value", p.Forecast),
                ("lowerBound", p.LowerBound),
                ("upperBound", p.UpperBound))).ToList();

            var link = await PersistAsync(
                tenantId,
                ReportArtifact.KindForecast,
                $"Dự báo {metric} ({platform})",
                platform,
                metric,
                points.Count > 0 ? DateOnly.FromDateTime(points[0].At.Date) : ReportAgentRunner.Today(),
                points.Count > 0 ? DateOnly.FromDateTime(points[^1].At.Date) : ReportAgentRunner.Today(),
                new ReportArtifactPayload(
                    ReportArtifact.KindForecast,
                    [
                        new ReportColumn("date", "Ngày", "date"),
                        new ReportColumn("value", "Dự báo", "number"),
                        new ReportColumn("lowerBound", "Cận dưới", "number"),
                        new ReportColumn("upperBound", "Cận trên", "number"),
                    ],
                    items,
                    new ReportChart("date", ["value", "lowerBound", "upperBound"])),
                ct).ConfigureAwait(false);

            return Json(new
            {
                operation = "forecast",
                metric,
                platform,
                total = items.Count,
                items,
                reportId = link?.Id,
                reportUrl = link?.Url,
            });
        }

        // Phân giải khoảng ngày cho KPI (ngày lẻ, tuần này, tháng này, lookback_days, from/to)
        var (fromDate, toDate) = ReportAgentRunner.ResolveKpiRange(
            AgentTaskInput.OptionalString(input, "date"),
            AgentTaskInput.OptionalString(input, "from_date") ?? AgentTaskInput.OptionalString(input, "fromDate"),
            AgentTaskInput.OptionalString(input, "to_date") ?? AgentTaskInput.OptionalString(input, "toDate"),
            OptionalInt(input, "lookback_days") ?? OptionalInt(input, "lookbackDays"));

        var report = await _runner.KpiSnapshotAsync(tenantId, fromDate, toDate, platform, ct).ConfigureAwait(false);
        var rows = report.PlatformRows;

        var snapshotItems = rows.Select(r => Row(
            ("platform", r.Platform),
            ("leads", r.Leads),
            ("dms", r.Dms),
            ("replies", r.Replies),
            ("conversions", r.Conversions),
            ("avgResponseTimeSec", r.AvgResponseTimeSec))).ToList();

        var isSingleDay = fromDate == toDate;
        var title = isSingleDay
            ? $"Báo cáo KPI ngày {ReportAgentRunner.FormatDate(fromDate)}"
            : $"Báo cáo KPI {ReportAgentRunner.FormatDate(fromDate)} - {ReportAgentRunner.FormatDate(toDate)}";

        var chart = new ReportChart("platform", ["leads", "dms", "conversions"]);

        var snapshotLink = await PersistAsync(
            tenantId,
            ReportArtifact.KindSnapshot,
            title,
            platform,
            metric: null,
            fromDate,
            toDate,
            new ReportArtifactPayload(
                ReportArtifact.KindSnapshot,
                [
                    new ReportColumn("platform", "Nền tảng", "text"),
                    new ReportColumn("leads", "Lead", "number"),
                    new ReportColumn("dms", "Tin nhắn", "number"),
                    new ReportColumn("replies", "Phản hồi", "number"),
                    new ReportColumn("conversions", "Chuyển đổi", "number"),
                    new ReportColumn("avgResponseTimeSec", "Phản hồi TB (giây)", "number"),
                ],
                snapshotItems,
                chart),
            ct).ConfigureAwait(false);

        return Json(new
        {
            operation = "snapshot",
            from = ReportAgentRunner.FormatDate(fromDate),
            to = ReportAgentRunner.FormatDate(toDate),
            date = isSingleDay ? ReportAgentRunner.FormatDate(fromDate) : $"{ReportAgentRunner.FormatDate(fromDate)} -> {ReportAgentRunner.FormatDate(toDate)}",
            platform,
            total = rows.Count,
            totalLeads = report.TotalLeads,
            totalDms = report.TotalDms,
            totalReplies = report.TotalReplies,
            totalConversions = report.TotalConversions,
            avgResponseTimeSec = report.AvgResponseTimeSec,
            items = rows,
            dailyTrends = report.DailyTrends,
            reportId = snapshotLink?.Id,
            reportUrl = snapshotLink?.Url,
        });
    }

    /// <summary>
    /// Hiệu suất nội dung đã đăng — phần báo cáo dành cho marketing. Mặc định 7 ngày gần nhất vì
    /// một ngày lẻ thường không có bài nào đăng, khác hẳn snapshot KPI vốn tính theo đúng một ngày.
    /// </summary>
    private async Task<string> ContentSnapshotAsync(
        Guid tenantId,
        string platform,
        IReadOnlyDictionary<string, string> input,
        CancellationToken ct)
    {
        var (fromDate, toDate) = ReportAgentRunner.ResolveRange(
            AgentTaskInput.OptionalString(input, "date"),
            OptionalInt(input, "lookback_days"),
            DefaultContentSnapshotDays);

        var rows = await _runner.ContentSnapshotAsync(tenantId, fromDate, toDate, platform, ct).ConfigureAwait(false);
        var items = rows.Select(r => Row(
            ("platform", r.Platform),
            ("postsPublished", r.PostsPublished),
            ("likes", r.Likes),
            ("comments", r.Comments),
            ("reactionsTotal", r.ReactionsTotal))).ToList();

        var link = await PersistAsync(
            tenantId,
            ReportArtifact.KindContentSnapshot,
            $"Hiệu suất nội dung {ReportAgentRunner.FormatDate(fromDate)} - {ReportAgentRunner.FormatDate(toDate)}",
            platform,
            metric: null,
            fromDate,
            toDate,
            new ReportArtifactPayload(
                ReportArtifact.KindContentSnapshot,
                [
                    new ReportColumn("platform", "Nền tảng", "text"),
                    new ReportColumn("postsPublished", "Bài đã đăng", "number"),
                    new ReportColumn("likes", "Lượt thích", "number"),
                    new ReportColumn("comments", "Bình luận", "number"),
                    new ReportColumn("reactionsTotal", "Tổng cảm xúc", "number"),
                ],
                items,
                new ReportChart("platform", ["postsPublished", "likes", "comments"])),
            ct).ConfigureAwait(false);

        return Json(new
        {
            operation = ReportArtifact.KindContentSnapshot,
            from = ReportAgentRunner.FormatDate(fromDate),
            to = ReportAgentRunner.FormatDate(toDate),
            platform,
            // platformCount chứ không phải "total": xem ghi chú ở nhánh phễu.
            platformCount = items.Count,
            postsPublished = rows.Sum(r => r.PostsPublished),
            likes = rows.Sum(r => r.Likes),
            comments = rows.Sum(r => r.Comments),
            reactionsTotal = rows.Sum(r => r.ReactionsTotal),
            items,
            reportId = link?.Id,
            reportUrl = link?.Url,
        });
    }

    /// <summary>
    /// Phễu duyệt nội dung — bài đang tắc ở khâu nào. Không chặn ngày trừ khi người dùng nêu rõ
    /// lookback_days: đây là ảnh chụp tồn đọng, mà bài kẹt lâu nhất lại nằm ngoài mọi cửa sổ gần đây.
    /// </summary>
    private async Task<string> ContentFunnelAsync(
        Guid tenantId,
        string platform,
        IReadOnlyDictionary<string, string> input,
        CancellationToken ct)
    {
        var lookbackDays = OptionalInt(input, "lookback_days");
        var toDate = ReportAgentRunner.ParseDate(AgentTaskInput.OptionalString(input, "date") ?? string.Empty);
        DateOnly? fromDate = lookbackDays is > 0
            ? ReportAgentRunner.ResolveRange(
                ReportAgentRunner.FormatDate(toDate), lookbackDays, lookbackDays.Value).From
            : null;

        var report = await _runner.ContentFunnelAsync(tenantId, fromDate, toDate, platform, ct).ConfigureAwait(false);
        var rows = report.Rows;
        var items = rows.Select(r => Row(
            ("platform", r.Platform),
            ("awaitingAgentReview", r.AwaitingAgentReview),
            ("agentReviewRunning", r.AgentReviewRunning),
            ("agentReviewNonPass", r.AgentReviewNonPass),
            ("reviewFailed", r.ReviewFailed),
            ("awaitingHumanApproval", r.AwaitingHumanApproval),
            ("approvedAwaitingSchedule", r.ApprovedAwaitingSchedule),
            ("scheduled", r.Scheduled),
            ("published", r.Published),
            ("rejected", r.Rejected),
            ("total", r.Total))).ToList();

        var link = await PersistAsync(
            tenantId,
            ReportArtifact.KindContentFunnel,
            fromDate is { } since
                ? $"Phễu duyệt nội dung {ReportAgentRunner.FormatDate(since)} - {ReportAgentRunner.FormatDate(toDate)}"
                : $"Phễu duyệt nội dung tính đến {ReportAgentRunner.FormatDate(toDate)}",
            platform,
            metric: null,
            fromDate ?? toDate,
            toDate,
            new ReportArtifactPayload(
                ReportArtifact.KindContentFunnel,
                [
                    new ReportColumn("platform", "Nền tảng", "text"),
                    new ReportColumn("awaitingAgentReview", "Chờ agent review", "number"),
                    new ReportColumn("agentReviewRunning", "Đang review", "number"),
                    new ReportColumn("agentReviewNonPass", "Agent không duyệt", "number"),
                    new ReportColumn("reviewFailed", "Review lỗi", "number"),
                    new ReportColumn("awaitingHumanApproval", "Chờ người duyệt", "number"),
                    new ReportColumn("approvedAwaitingSchedule", "Đã duyệt, chờ lên lịch", "number"),
                    new ReportColumn("scheduled", "Đã lên lịch", "number"),
                    new ReportColumn("published", "Đã đăng", "number"),
                    new ReportColumn("rejected", "Bị từ chối", "number"),
                    new ReportColumn("total", "Tổng bài", "number"),
                ],
                items,
                new ReportChart("platform", ["awaitingHumanApproval", "scheduled", "published"])),
            ct).ConfigureAwait(false);

        return Json(new
        {
            operation = ReportArtifact.KindContentFunnel,
            from = fromDate is { } start ? ReportAgentRunner.FormatDate(start) : null,
            to = ReportAgentRunner.FormatDate(toDate),
            platform,
            // platformCount chứ không phải "total": một trường tên total nằm cạnh danh sách bài rất dễ
            // bị LLM đọc thành số bài rồi viết ra câu "có 1 bài" trong khi đó là 1 nền tảng.
            platformCount = items.Count,
            totalItems = rows.Sum(r => r.Total),
            awaitingHumanApproval = rows.Sum(r => r.AwaitingHumanApproval),
            published = rows.Sum(r => r.Published),
            truncated = report.Truncated,
            truncatedNote = report.Truncated
                ? $"Chỉ tính {report.Cap} bài mới nhất; tổng thực tế cao hơn — phải nói rõ điều này khi báo cáo."
                : null,
            items,
            reportId = link?.Id,
            reportUrl = link?.Url,
        });
    }

    /// <summary>
    /// Chốt artifact và trả link để LLM dán vào narrative. Không có dòng nào thì không lưu: một link
    /// dẫn tới bảng rỗng còn tệ hơn không có link, và LLM vẫn thấy total = 0 để nói đúng sự thật.
    /// </summary>
    private async Task<(Guid Id, string Url)?> PersistAsync(
        Guid tenantId,
        string kind,
        string title,
        string platform,
        string? metric,
        DateOnly fromDate,
        DateOnly toDate,
        ReportArtifactPayload payload,
        CancellationToken ct)
    {
        if (payload.Rows.Count == 0)
            return null;

        var id = await _runner.SaveArtifactAsync(
            tenantId, kind, title, platform, metric, fromDate, toDate, payload, ct).ConfigureAwait(false);
        return (id, string.Create(CultureInfo.InvariantCulture, $"/reports/{id}"));
    }

    private const int DefaultContentSnapshotDays = 7;

    /// <summary>
    /// Tên operation do LLM sinh nên hay lệch; nhận thêm bí danh thay vì để rơi về snapshot KPI.
    /// Suy từ description CHỈ khi không có operation, và chỉ khi câu lệnh nói rõ về nội dung/bài đăng —
    /// đoán rộng hơn sẽ biến một yêu cầu báo cáo sale hợp lệ thành báo cáo marketing.
    /// </summary>
    internal static string NormalizeOperation(string? operation, string? description)
    {
        var normalized = (operation ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_');
        normalized = normalized switch
        {
            "content" or "content_snapshot" or "marketing" or "engagement" or "posts"
                => ReportArtifact.KindContentSnapshot,
            "content_funnel" or "funnel" or "content_pipeline" or "pipeline"
                => ReportArtifact.KindContentFunnel,
            _ => normalized,
        };

        if (normalized.Length > 0)
            return normalized;

        return LooksLikeContentGoal(description) ? ReportArtifact.KindContentSnapshot : "snapshot";
    }

    private static bool LooksLikeContentGoal(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return false;
        var d = description.ToLowerInvariant();
        return d.Contains("bài đăng", StringComparison.Ordinal)
            || d.Contains("bai dang", StringComparison.Ordinal)
            || d.Contains("nội dung", StringComparison.Ordinal)
            || d.Contains("noi dung", StringComparison.Ordinal)
            || d.Contains("tương tác", StringComparison.Ordinal)
            || d.Contains("tuong tac", StringComparison.Ordinal)
            || d.Contains("marketing", StringComparison.Ordinal)
            || d.Contains("engagement", StringComparison.Ordinal)
            || d.Contains("content", StringComparison.Ordinal);
    }

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells)
    {
        var row = new Dictionary<string, object?>(cells.Length, StringComparer.Ordinal);
        foreach (var (key, value) in cells)
            row[key] = value;
        return row;
    }

    private static int? OptionalInt(IReadOnlyDictionary<string, string> input, string key) =>
        input.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static double? OptionalDouble(IReadOnlyDictionary<string, string> input, string key) =>
        input.TryGetValue(key, out var value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
