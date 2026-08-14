namespace Clawbot.Api.Contracts.Documents;

// RecipientEmail: gửi thẳng tới email người dùng gõ tay, không bắt buộc phải có contact trong CRM.
public sealed record GenerateDocumentRequest(
    string TemplateCode,
    Guid? ContactId,
    IReadOnlyDictionary<string, string>? Vars,
    string? SentVia,
    string? RecipientEmail = null);

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
    string? SentVia,
    string? RecipientEmail = null);

public sealed record GenerateDocumentKitResponse(
    IReadOnlyList<GenerateDocumentResponse> Documents,
    int TotalSizeBytes,
    long TotalLatencyMs);

// Mô tả một trường điền của mẫu — frontend dựng form nhập liệu từ danh sách này.
public sealed record TemplateFieldDto(
    string Key,
    string Label,
    string Type,
    bool Required,
    string? Placeholder,
    string? Sample);

public sealed record DocumentTemplateDto(
    Guid Id,
    string Code,
    string DocType,
    string TemplateHtml,
    IReadOnlyList<TemplateFieldDto> Fields,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateDocumentTemplateRequest(
    string Code,
    string DocType,
    string TemplateHtml,
    IReadOnlyList<TemplateFieldDto>? Fields = null);

public sealed record UpdateDocumentTemplateRequest(
    string DocType,
    string TemplateHtml,
    IReadOnlyList<TemplateFieldDto>? Fields = null);

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
