using Clawbot.Domain.Agents;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.AgentService.Services;

// Ghi mỗi lượt review nội dung thành một AgentSession + AgentTrace để người dùng theo dõi được ở
// màn "Phiên điều phối" (/agents/runs) — trước đây review chỉ nằm trong content_review_tasks nên
// bấm "Xếp lại agent review" xong không có chỗ nào xem quá trình chạy.
//
// Session ở đây thuần tuý là NHẬT KÝ: không có plan, không phê duyệt, không can thiệp được. Vòng đời
// review vẫn do content_review_tasks quyết định; recorder hỏng KHÔNG được làm hỏng review, nên mọi
// lỗi ghi nhật ký đều bị nuốt (best-effort) thay vì ném lên coordinator.
public interface IContentReviewSessionRecorder
{
    Task<Guid?> StartAsync(
        Guid tenantId,
        Guid contentItemId,
        Guid reviewerAgentId,
        string platform,
        string body,
        int contentRevision,
        int reviewCycle,
        CancellationToken cancellationToken);

    Task TraceAsync(
        Guid? sessionId,
        string phase,
        string message,
        CancellationToken cancellationToken);

    Task FinishAsync(
        Guid? sessionId,
        string reviewStatus,
        string? reason,
        CancellationToken cancellationToken);
}

// Nhật ký PHẢI dùng DbContext riêng (scope tự mở), không dùng chung với coordinator: coordinator ghi
// review trong transaction + ChangeTracker của nó, nên một entity nhật ký lỗi (vd vi phạm FK) sẽ kẹt ở
// trạng thái Added và kéo sập luôn SaveChanges của chính lượt review — nhật ký hỏng làm hỏng nghiệp vụ.
public sealed partial class ContentReviewSessionRecorder(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<ContentReviewSessionRecorder>? logger = null) : IContentReviewSessionRecorder
{
    // Goal mang tiền tố cố định để màn runs nhận diện được nhóm phiên này; hậu tố là mã bài rút gọn
    // giúp phân biệt khi một tenant chạy nhiều lượt review song song.
    public const string GoalPrefix = "Duyệt nội dung";

    // Trace message dài (verdict LLM) cắt bớt: cột message không giới hạn nhưng UI timeline chỉ đọc
    // được đoạn ngắn, và trace không phải nơi lưu bản đầy đủ (bản đầy đủ nằm ở content_items.reason).
    private const int MaxTraceMessageLength = 2000;

    // Cột agent_sessions.goal là nvarchar(256) — vượt sẽ ném khi SaveChanges, phải cắt trước.
    private const int MaxGoalLength = 250;
    private const int MaxGoalSnippetLength = 120;

    private readonly ILogger<ContentReviewSessionRecorder>? _logger = logger;

    public async Task<Guid?> StartAsync(
        Guid tenantId,
        Guid contentItemId,
        Guid reviewerAgentId,
        string platform,
        string body,
        int contentRevision,
        int reviewCycle,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = clock.UtcNow;
            // agent_sessions.agent_id có FK sang dbo.agents, trong khi reviewer ở đây là AgentDefinition
            // (bảng agent_definitions) — gán vào sẽ vi phạm FK. Danh tính reviewer ghi trong trace thay vì cột này.
            var session = AgentSession.Start(
                tenantId,
                agentId: null,
                conversationId: null,
                BuildGoal(platform, body),
                now);
            session.AppendTrace(
                taskId: "review",
                agentName: "reviewer-agent",
                phase: "start",
                message: $"Bắt đầu duyệt bài trên {(string.IsNullOrWhiteSpace(platform) ? "kênh không rõ" : platform.Trim())} "
                    + $"(revision {contentRevision}, lượt {reviewCycle}, reviewer {reviewerAgentId:N}).",
                now);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AgentSessions.Add(session);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return session.Id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRecorderFailed(ex, nameof(StartAsync));
            return null;
        }
    }

    public async Task TraceAsync(
        Guid? sessionId,
        string phase,
        string message,
        CancellationToken cancellationToken)
    {
        if (sessionId is null)
            return;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.AgentSessions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == sessionId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (session is null)
                return;

            session.AppendTrace(
                taskId: "review",
                agentName: "reviewer-agent",
                phase,
                Truncate(message),
                clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRecorderFailed(ex, nameof(TraceAsync));
        }
    }

    public async Task FinishAsync(
        Guid? sessionId,
        string reviewStatus,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (sessionId is null)
            return;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.AgentSessions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Id == sessionId.Value, cancellationToken)
                .ConfigureAwait(false);
            if (session is null || session.Status != AgentSessionStatuses.Running)
                return;

            var now = clock.UtcNow;
            session.AppendTrace(
                taskId: "review",
                agentName: "reviewer-agent",
                phase: "verdict",
                Truncate($"{VerdictLabel(reviewStatus)}{(string.IsNullOrWhiteSpace(reason) ? "" : $": {reason}")}"),
                now);

            // failed = reviewer gặp sự cố kỹ thuật -> phiên hỏng. Còn passed/rejected/needs_human đều là
            // KẾT LUẬN hợp lệ của reviewer nên phiên coi như chạy xong, không phải thất bại.
            if (string.Equals(reviewStatus, ContentItem.ReviewStatusFailed, StringComparison.Ordinal))
                session.Fail(now);
            else
                session.Finish(now);

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRecorderFailed(ex, nameof(FinishAsync));
        }
    }

    // Goal là thứ duy nhất hiện ở bảng /agents/runs nên phải đọc được bằng mắt: nền tảng + đoạn đầu
    // bài. Body đã qua PII redact ở đường tạo nội dung; ở đây chỉ cắt ngắn và bỏ xuống dòng.
    private static string BuildGoal(string platform, string body)
    {
        var snippet = string.Join(' ', (body ?? string.Empty).Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (snippet.Length > MaxGoalSnippetLength)
            snippet = string.Concat(snippet[..MaxGoalSnippetLength].TrimEnd(), "...");

        var channel = string.IsNullOrWhiteSpace(platform) ? "nội dung" : platform.Trim();
        var goal = string.IsNullOrEmpty(snippet)
            ? $"{GoalPrefix} ({channel})"
            : $"{GoalPrefix} ({channel}): {snippet}";
        return goal.Length <= MaxGoalLength ? goal : goal[..MaxGoalLength];
    }

    private static string VerdictLabel(string reviewStatus) => reviewStatus switch
    {
        ContentItem.ReviewStatusPassed => "Agent duyệt đạt",
        ContentItem.ReviewStatusRejected => "Agent từ chối",
        ContentItem.ReviewStatusNeedsHuman => "Chuyển người duyệt",
        ContentItem.ReviewStatusFailed => "Reviewer gặp sự cố",
        _ => reviewStatus,
    };

    private static string Truncate(string message) =>
        string.IsNullOrEmpty(message) || message.Length <= MaxTraceMessageLength
            ? message
            : message[..MaxTraceMessageLength];

    private void LogRecorderFailed(Exception ex, string stage)
    {
        // Nhật ký hỏng không được kéo theo review: chỉ cảnh báo rồi đi tiếp.
        if (_logger is not null)
            LogRecorderFailure(_logger, stage, ex);
    }

    [LoggerMessage(EventId = 7401, Level = LogLevel.Warning,
        Message = "Ghi nhật ký phiên duyệt nội dung thất bại ở bước {Stage}; review vẫn chạy bình thường.")]
    private static partial void LogRecorderFailure(ILogger logger, string stage, Exception exception);
}
