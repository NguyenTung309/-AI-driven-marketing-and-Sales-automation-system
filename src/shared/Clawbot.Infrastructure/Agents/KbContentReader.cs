using Clawbot.Agents.Core.Rag;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Agents;

// Đọc ContentMd các KB version deployed cho LlmRagRetriever. IgnoreQueryFilters + tenantId tường minh:
// retriever chạy trong scope gRPC/job không có HTTP tenant context (hangfire-job-scope-has-no-tenant).
public sealed class KbContentReader(AppDbContext db) : IKbContentReader
{
    public async Task<IReadOnlyList<KbActiveContent>> GetActiveContentAsync(Guid tenantId, string? kbModuleCode, CancellationToken ct = default)
    {
        var query =
            from version in db.KbVersions.IgnoreQueryFilters().AsNoTracking()
            join module in db.KbModules.IgnoreQueryFilters().AsNoTracking() on version.KbModuleId equals module.Id
            where module.TenantId == tenantId
                && module.DeletedAt == null
                && version.Status == "deployed"
            select new { version.Id, module.Code, version.ContentMd };

        if (!string.IsNullOrWhiteSpace(kbModuleCode))
            query = query.Where(row => row.Code == kbModuleCode);

        var rows = await query.ToListAsync(ct).ConfigureAwait(false);
        // ToString() ngoài SQL: CONVERT của SQL Server trả GUID UPPERCASE, lệch với id lowercase ở Qdrant/trace.
        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.ContentMd))
            .Select(row => new KbActiveContent(row.Id.ToString(), row.Code, row.ContentMd))
            .ToList();
    }
}
