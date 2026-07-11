using Clawbot.Agents.Core.Kb;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Learning;

// Biến 1 KbSuggestion ĐÃ approved (auto hoặc người) thành KbVersion deploy thật — đi đúng luồng
// kb.deploy sẵn có: archive bản deployed cũ, Deploy bản mới, embed + upsert Qdrant.
// Dùng chung cho nhánh auto trong KnowledgeDistillationJob và endpoint approve của người.
// Không sealed: test job auto-approve override MaterializeAsync (KbDeployService cần Qdrant thật).
public class KbSuggestionMaterializer(AppDbContext db, KbDeployService deployService, IClock clock)
{
    // virtual để test job auto-approve fake được bước deploy/embed (KbDeployService cần Qdrant thật).
    public virtual async Task<KbVersion> MaterializeAsync(KbSuggestion suggestion, CancellationToken ct = default)
    {
        if (suggestion.Status != KbSuggestion.StatusApproved)
            throw new InvalidOperationException($"materialize_requires_approved:{suggestion.Status}");

        var now = clock.UtcNow;
        KbModule? module;
        if (suggestion.TargetKbModuleId is { } targetId)
        {
            module = await db.KbModules.IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.Id == targetId && m.TenantId == suggestion.TenantId, ct)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("target_module_not_found");
        }
        else
        {
            var code = SlugifyModuleCode(suggestion.Title);
            module = await db.KbModules.IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.TenantId == suggestion.TenantId && m.Code == code, ct)
                .ConfigureAwait(false);
            if (module is null)
            {
                module = KbModule.Create(suggestion.TenantId, code, suggestion.Title, now);
                db.KbModules.Add(module);
            }
        }

        var nextVersion = await db.KbVersions.IgnoreQueryFilters()
            .Where(v => v.KbModuleId == module.Id)
            .MaxAsync(v => (int?)v.Version, ct).ConfigureAwait(false) ?? 0;
        var version = KbVersion.Create(module.Id, nextVersion + 1, suggestion.ContentMd, now);
        db.KbVersions.Add(version);

        var previous = await db.KbVersions.IgnoreQueryFilters()
            .Where(v => v.KbModuleId == module.Id && v.Status == "deployed")
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var prev in previous)
            db.Entry(prev).Property("Status").CurrentValue = "archived";

        version.Deploy(now);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await deployService.EmbedAndUpsertAsync(version, module.Code, suggestion.TenantId, ct).ConfigureAwait(false);
        return version;
    }

    // Bản sao nhỏ của KbEndpoints.SlugifyModuleCode (Api) — Infrastructure không tham chiếu được Api.
    internal static string SlugifyModuleCode(string raw)
    {
        var slug = new string(raw.Trim().ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')
            .ToArray()).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return string.IsNullOrEmpty(slug) ? $"kb-{Guid.NewGuid():N}"[..11] : slug;
    }
}
