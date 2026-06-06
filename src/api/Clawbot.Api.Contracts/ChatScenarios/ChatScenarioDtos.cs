namespace Clawbot.Api.Contracts.ChatScenarios;

public sealed record ChatScenarioDto(
    Guid Id,
    string Code,
    string GroupName,
    string TriggerText,
    string ResponseTemplate,
    string? ToneVoice,
    string Platforms,
    decimal? SuccessRate);

public sealed record CreateChatScenarioRequest(
    string Code,
    string GroupName,
    string TriggerText,
    string ResponseTemplate,
    string Platforms,
    string? ToneVoice);

public sealed record UpdateChatScenarioRequest(
    string GroupName,
    string TriggerText,
    string ResponseTemplate,
    string Platforms,
    string? ToneVoice);

public sealed record MatchScenarioRequest(string Text, string? Platform);

public sealed record MatchScenarioResponse(ChatScenarioDto? Match);

public sealed record RecordOutcomeRequest(bool Converted);
