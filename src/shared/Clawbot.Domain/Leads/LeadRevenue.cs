using Clawbot.Domain.Common;

namespace Clawbot.Domain.Leads;

public sealed class LeadRevenue : AggregateRoot<Guid>, ITenantOwned
{
    public const string SourceManual = "manual";
    public const string SourceAi = "ai";

    public const string StatusPending = "pending";
    public const string StatusApproved = "approved";
    public const string StatusRejected = "rejected";

    /// <summary>Trần nghiệp vụ VND (DECIMAL(18,2) rộng hơn nhiều) — chặn prompt-injection số ảo.</summary>
    public const decimal MaxAmount = 10_000_000_000m;

    public Guid TenantId { get; private set; }
    public Guid LeadId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "VND";
    public string Source { get; private set; } = SourceManual;
    public string Status { get; private set; } = StatusPending;
    public string? Evidence { get; private set; }
    public Guid? ProposedBy { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }

    private LeadRevenue() { }

    public static LeadRevenue CreateManual(
        Guid tenantId,
        Guid leadId,
        decimal amount,
        string currency,
        Guid byUserId,
        DateTimeOffset at)
    {
        ValidateAmount(amount);
        return new LeadRevenue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeadId = leadId,
            Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
            Currency = NormalizeCurrency(currency),
            Source = SourceManual,
            Status = StatusApproved,
            ProposedBy = byUserId,
            DecidedBy = byUserId,
            CreatedAt = at,
            DecidedAt = at,
        };
    }

    public static LeadRevenue ProposeByAi(
        Guid tenantId,
        Guid leadId,
        decimal amount,
        string currency,
        string? evidence,
        DateTimeOffset at)
    {
        ValidateAmount(amount);
        return new LeadRevenue
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LeadId = leadId,
            Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
            Currency = NormalizeCurrency(currency),
            Source = SourceAi,
            Status = StatusPending,
            Evidence = NormalizeEvidence(evidence),
            CreatedAt = at,
        };
    }

    public void Approve(Guid? byUserId, decimal? amendedAmount, DateTimeOffset at)
    {
        if (Status != StatusPending)
            return;
        if (amendedAmount.HasValue)
        {
            ValidateAmount(amendedAmount.Value);
            Amount = decimal.Round(amendedAmount.Value, 2, MidpointRounding.AwayFromZero);
        }

        Status = StatusApproved;
        DecidedBy = byUserId;
        DecidedAt = at;
    }

    public void Reject(Guid byUserId, DateTimeOffset at)
    {
        if (Status != StatusPending)
            return;

        Status = StatusRejected;
        DecidedBy = byUserId;
        DecidedAt = at;
    }

    /// <summary>
    /// Auto-approve chỉ khi evidence chứa/tham chiếu số tiền (grounding deterministic),
    /// không tin LLM thô từ transcript khách kiểm soát.
    /// </summary>
    public static bool EvidenceGroundsAmount(decimal amount, string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence) || amount <= 0)
            return false;

        var amountDigits = new string(
            decimal.Truncate(amount).ToString(System.Globalization.CultureInfo.InvariantCulture)
                .Where(char.IsDigit).ToArray());
        if (amountDigits.Length < 4)
            return false;

        var evidenceDigits = new string(evidence.Where(char.IsDigit).ToArray());
        if (evidenceDigits.Contains(amountDigits, StringComparison.Ordinal))
            return true;

        // Cho phép evidence viết 5.000.000 / 5,000,000
        var grouped = decimal.Truncate(amount)
            .ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"));
        if (evidence.Contains(grouped, StringComparison.Ordinal))
            return true;

        var plainGrouped = decimal.Truncate(amount)
            .ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        return evidence.Contains(plainGrouped, StringComparison.Ordinal);
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "amount_must_be_positive");
        if (amount > MaxAmount)
            throw new ArgumentOutOfRangeException(nameof(amount), "amount_too_large");
        if (decimal.Round(amount, 2, MidpointRounding.AwayFromZero) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount), "amount_scale_invalid");
    }

    private static string NormalizeCurrency(string currency)
    {
        var normalized = string.IsNullOrWhiteSpace(currency)
            ? "VND"
            : currency.Trim().ToUpperInvariant();
        if (normalized != "VND")
            throw new ArgumentException("unsupported_currency", nameof(currency));
        return normalized;
    }

    private static string? NormalizeEvidence(string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
            return null;

        var trimmed = evidence.Trim();
        return trimmed.Length <= 1_000 ? trimmed : trimmed[..1_000];
    }
}
