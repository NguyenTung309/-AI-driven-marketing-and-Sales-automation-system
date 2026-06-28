using Clawbot.Agents.Core.Skills;

namespace Clawbot.Agents.Core.Skills.Lead;

// Canonical lead-interest event codes emitted from a customer message. These match the default
// LeadScoringRule seed (see LeadScoringDefaults) so the scoring engine can weight them.
public static class LeadSignalCodes
{
    public const string AskedSubstantiveQuestion = "asked_substantive_question";
    public const string AskedClassSize = "asked_class_size";
    public const string AskedSchedule = "asked_schedule";
    public const string AskedTeacher = "asked_teacher";
    public const string AskedCommitment = "asked_commitment";
    public const string AskedPrice = "asked_price";
    public const string PurchaseIntent = "purchase_intent";

    public static readonly IReadOnlyList<string> All =
    [
        AskedSubstantiveQuestion, AskedClassSize, AskedSchedule,
        AskedTeacher, AskedCommitment, AskedPrice, PurchaseIntent,
    ];
}

public sealed record LeadSignalResult(IReadOnlyList<string> EventCodes);

// Classifies a single inbound customer message into zero or more lead-interest signals.
// Pure "vâng ạ" / "để em xem" style acknowledgements yield no codes.
public interface ILeadSignalClassifier : ISkill
{
    Task<LeadSignalResult> ClassifyAsync(string message, string? locale, CancellationToken ct = default);
}
