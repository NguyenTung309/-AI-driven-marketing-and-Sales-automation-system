using Clawbot.Domain.Common;

namespace Clawbot.Domain.Tenants;

public sealed class Tenant : AggregateRoot<Guid>
{
    public string Slug { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string PlanName { get; private set; } = "free";
    public bool IsActive { get; private set; } = true;
    public string? BrandName { get; private set; }
    public string? LogoUrl { get; private set; }
    public string? PrimaryColor { get; private set; }
    public string? AccentColor { get; private set; }
    public string? SupportName { get; private set; }
    public string? WidgetGreeting { get; private set; }
    public bool RequireOrchestrationApproval { get; private set; }
    // Review-gate P1 (QĐ1: default OFF, opt-in per tenant): khi bật, item chỉ được publish khi có
    // chữ ký reviewer agent (ContentItem.ApprovedByAgentId).
    public bool RequireContentReview { get; private set; }
    // Review-gate P3 manual-mode: khi bật, MỌI AI reply hold thành pending_approval chờ người duyệt
    // (không gửi tự động); tin sale gõ tay miễn (QĐ5).
    public bool RequireChatReplyApproval { get; private set; }
    // Bypass review-gate P2 (QĐ user 2026-07-16): bật = AI reply gửi thẳng, KHÔNG qua critic chấm
    // giá/cam kết. Default OFF (fail-closed). Safety cứng (toxicity/injection/echo) vẫn giữ nguyên.
    public bool SkipChatReplyReview { get; private set; }
    // Gate tri thức tự học: default OFF = AI tự duyệt kb_suggestions khi rail đạt (verdict approve +
    // accuracy không giảm). Bật = mọi đề xuất chờ người duyệt (QĐ 2026-07-11, ngược chiều 2 flag trên).
    public bool RequireKbHumanReview { get; private set; }
    // Hạn mức chi tiêu LLM mỗi tháng (USD). null = dùng mặc định hệ thống.
    public decimal? MonthlyCostCapUsd { get; private set; }
    // Sale gửi tay -> AI tạm nhường bao lâu (phút) rồi tự bật lại. Cấu hình per-tenant, mặc định 5.
    public int AiAutoReplyResumeMinutes { get; private set; } = 5;
    // Hội thoại mở không hoạt động quá ngưỡng này (phút) thì cảnh báo sale; quá gấp đôi thì
    // escalate Trưởng phòng KD. Cấu hình per-tenant, mặc định 5.
    public int IdleAlertMinutes { get; private set; } = 5;
    public DateTimeOffset CreatedAt { get; private set; }

    private Tenant() { }

    public static Tenant Create(string slug, string displayName, string planName, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            DisplayName = displayName,
            PlanName = planName,
            IsActive = true,
            CreatedAt = createdAt,
        };

    public void UpdateBranding(
        string? brandName,
        string? logoUrl,
        string? primaryColor,
        string? accentColor,
        string? supportName,
        string? widgetGreeting)
    {
        BrandName = NormalizeNullable(brandName);
        LogoUrl = NormalizeNullable(logoUrl);
        PrimaryColor = NormalizeNullable(primaryColor);
        AccentColor = NormalizeNullable(accentColor);
        SupportName = NormalizeNullable(supportName);
        WidgetGreeting = NormalizeNullable(widgetGreeting);
    }

    public void SetRequireOrchestrationApproval(bool requireApproval) =>
        RequireOrchestrationApproval = requireApproval;

    public void SetRequireContentReview(bool requireReview) =>
        RequireContentReview = requireReview;

    public void SetRequireChatReplyApproval(bool requireApproval) =>
        RequireChatReplyApproval = requireApproval;

    public void SetSkipChatReplyReview(bool skip) =>
        SkipChatReplyReview = skip;

    public void SetRequireKbHumanReview(bool requireReview) =>
        RequireKbHumanReview = requireReview;

    // null hoặc <= 0 → xoá hạn mức riêng, quay về mặc định hệ thống.
    public void SetMonthlyCostCapUsd(decimal? capUsd) =>
        MonthlyCostCapUsd = capUsd is > 0m ? capUsd : null;

    // <= 0 → về mặc định 5 phút; clamp trần 1 ngày để tránh cấu hình vô lý.
    public void SetAiAutoReplyResumeMinutes(int minutes) =>
        AiAutoReplyResumeMinutes = minutes <= 0 ? 5 : Math.Min(minutes, 1440);

    // <= 0 → về mặc định 5 phút; clamp trần 1 ngày để tránh cấu hình vô lý.
    public void SetIdleAlertMinutes(int minutes) =>
        IdleAlertMinutes = minutes <= 0 ? 5 : Math.Min(minutes, 1440);

    private static string? NormalizeNullable(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
