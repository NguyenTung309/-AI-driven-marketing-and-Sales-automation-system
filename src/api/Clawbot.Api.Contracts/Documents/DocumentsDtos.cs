namespace Clawbot.Api.Contracts.Documents;

public sealed record GenerateDocumentRequest(
    string TemplateCode,
    Guid? ContactId,
    IReadOnlyDictionary<string, string>? Vars,
    string? SentVia);

public sealed record GenerateDocumentResponse(
    Guid DocumentId,
    string FileUrl,
    string FileHash,
    int SizeBytes,
    long LatencyMs);

public sealed record GenerateDocumentKitRequest(
    IReadOnlyList<string>? TemplateCodes,
    Guid? ContactId,
    IReadOnlyDictionary<string, string>? Vars,
    string? SentVia);

public sealed record GenerateDocumentKitResponse(
    IReadOnlyList<GenerateDocumentResponse> Documents,
    int TotalSizeBytes,
    long TotalLatencyMs);

public sealed record DocumentTemplateDto(
    Guid Id,
    string Code,
    string DocType,
    string TemplateHtml,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateDocumentTemplateRequest(string Code, string DocType, string TemplateHtml);

public sealed record UpdateDocumentTemplateRequest(string DocType, string TemplateHtml);

public sealed record GeneratedDocumentDto(
    Guid Id,
    Guid TemplateId,
    Guid? ContactId,
    string FileUrl,
    string? FileHash,
    string? SentVia,
    DateTimeOffset? SentAt,
    DateTimeOffset? OpenedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt = null);
