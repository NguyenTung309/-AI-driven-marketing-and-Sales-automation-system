namespace Clawbot.Api.Contracts.KnowledgeBase;

public sealed record KbModuleDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string? OwnerRole,
    string Status,
    int VersionCount,
    int? LatestVersion,
    DateTimeOffset CreatedAt);

public sealed record CreateKbModuleRequest(string Code, string Name, string? Description, string? OwnerRole);

public sealed record UpdateKbModuleRequest(string Name, string? Description, string? OwnerRole);

public sealed record KbVersionDto(
    Guid Id,
    Guid KbModuleId,
    int Version,
    string Status,
    decimal? AccuracyScore,
    DateTimeOffset? DeployedAt,
    DateTimeOffset CreatedAt);

public sealed record KbVersionDetailDto(
    Guid Id,
    Guid KbModuleId,
    int Version,
    string Status,
    string ContentMd,
    decimal? AccuracyScore,
    DateTimeOffset? DeployedAt,
    DateTimeOffset CreatedAt);

public sealed record CreateKbVersionRequest(string ContentMd);

// Result of uploading a file (docx/xlsx/csv/pdf/txt/md) auto-converted to a draft KB version.
public sealed record KbUploadResult(
    KbVersionDto Version,
    string SourceFormat,
    int CharCount,
    string ContentMd);

// Per-file outcome of the auto-classify upload. Success=false → Error explains why
// (extraction_failed | llm_not_configured | classification_failed). Deployed=false with
// Error="deploy_failed" means the draft version was created but embedding failed.
public sealed record KbClassifiedFileDto(
    string FileName,
    bool Success,
    string? Error,
    Guid? ModuleId,
    string? ModuleCode,
    string? ModuleName,
    bool IsNewModule,
    double Confidence,
    string? Reason,
    KbVersionDto? Version,
    bool Deployed);

public sealed record KbClassifyUploadResponse(IReadOnlyList<KbClassifiedFileDto> Results);

public sealed record KbTestCaseDto(Guid Id, string Question, string ExpectedAnswer, bool IsActive);

public sealed record CreateKbTestCaseRequest(string Question, string ExpectedAnswer);

public sealed record GenerateKbTestCasesRequest(int? Count);

public sealed record KbTestRunResult(
    Guid VersionId,
    int Version,
    int TotalCases,
    int PassedCases,
    decimal AccuracyPercent,
    IReadOnlyList<KbTestCaseResult> Cases);

public sealed record KbTestCaseResult(Guid TestCaseId, string Question, bool Passed, string? Answer);

public sealed record KbAccuracySummary(
    Guid KbModuleId,
    string Code,
    string Name,
    int? LatestVersion,
    decimal? LatestAccuracyPercent,
    decimal? RollingAccuracyPercent,
    DateTimeOffset? LastTestedAt);

public sealed record KbVersionDiff(
    int FromVersion,
    int ToVersion,
    int LinesAdded,
    int LinesRemoved,
    string UnifiedDiff);
