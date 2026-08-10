namespace Clawbot.Agents.Core.Rag;

// Nội dung KB active (status=deployed) đọc thẳng từ SQL — cho LLM-mode retrieval không cần Qdrant.
public sealed record KbActiveContent(string KbVersionId, string ModuleCode, string ContentMd);

public interface IKbContentReader
{
    // moduleCodes rỗng/null = mọi module deployed của tenant.
    Task<IReadOnlyList<KbActiveContent>> GetActiveContentAsync(Guid tenantId, IReadOnlyCollection<string>? moduleCodes, CancellationToken ct = default);
}
