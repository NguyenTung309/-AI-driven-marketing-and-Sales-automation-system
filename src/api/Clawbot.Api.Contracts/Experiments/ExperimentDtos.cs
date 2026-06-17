namespace Clawbot.Api.Contracts.Experiments;

public sealed record ExperimentDto(
    Guid Id,
    string Code,
    string Name,
    string TargetType,
    Guid TargetId,
    string Status,
    IReadOnlyList<ExperimentVariantDto> Variants);

public sealed record ExperimentVariantDto(
    Guid Id,
    string Code,
    string Name,
    int Weight,
    Guid? ChatScenarioId,
    Guid? KbVersionId);

public sealed record CreateExperimentRequest(
    string Code,
    string Name,
    string TargetType,
    Guid TargetId,
    IReadOnlyList<CreateExperimentVariantRequest> Variants);

public sealed record CreateExperimentVariantRequest(
    string Code,
    string Name,
    int Weight,
    Guid? ChatScenarioId,
    Guid? KbVersionId);

public sealed record AssignExperimentRequest(string SubjectKey);

public sealed record RecordExperimentEventRequest(
    Guid VariantId,
    string SubjectKey,
    string EventType,
    decimal? Value);
