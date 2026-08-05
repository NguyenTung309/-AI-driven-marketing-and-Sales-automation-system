namespace Clawbot.Agents.Core.Rag;

public sealed record RagChunk(string KbVersionId, string KbModuleCode, string Snippet, float Score);

public sealed record RagRequest(
    Guid TenantId,
    string? KbModuleCode,
    string Query,
    int TopK = 5,
    // Danh sách module agent được liên kết (KbModulesJson từ cấu hình agent). Có giá trị -> chỉ
    // retrieve trong các module này; rỗng/null -> fallback KbModuleCode đơn; cả hai rỗng -> mọi module.
    IReadOnlyList<string>? KbModuleCodes = null)
{
    public IReadOnlyList<string> EffectiveModuleCodes =>
        KbModuleCodes is { Count: > 0 }
            ? KbModuleCodes
            : string.IsNullOrWhiteSpace(KbModuleCode) ? Array.Empty<string>() : [KbModuleCode];
}

public interface IRagRetriever
{
    Task<IReadOnlyList<RagChunk>> RetrieveAsync(RagRequest request, CancellationToken ct = default);
}
