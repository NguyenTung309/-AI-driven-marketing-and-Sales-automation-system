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

    private static string? NormalizeNullable(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
