using System.Text;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Kb;
using Clawbot.Agents.Core.Rag;
using Clawbot.Api.Contracts.KnowledgeBase;
using Clawbot.Api.Middleware;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class KbEndpoints
{
    public static IEndpointRouteBuilder MapKb(this IEndpointRouteBuilder app)
    {
        var modules = app.MapGroup("/api/kb/modules").RequireAuthorization().RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        modules.MapGet("/", ListModulesAsync);
        modules.MapGet("/{id:guid}", GetModuleAsync);
        modules.MapPost("/", CreateModuleAsync);
        modules.MapPut("/{id:guid}", UpdateModuleAsync);
        modules.MapPost("/{id:guid}/archive", ArchiveModuleAsync);

        modules.MapGet("/{id:guid}/versions", ListVersionsAsync);
        modules.MapPost("/{id:guid}/versions", CreateVersionAsync);
        modules.MapGet("/{id:guid}/versions/{versionId:guid}", GetVersionDetailAsync);
        modules.MapPost("/{id:guid}/versions/{versionId:guid}/deploy", DeployVersionAsync);
        modules.MapPost("/{id:guid}/versions/{versionId:guid}/rollback", RollbackToVersionAsync);
        modules.MapGet("/{id:guid}/diff", DiffVersionsAsync);

        modules.MapGet("/{id:guid}/test-cases", ListTestCasesAsync);
        modules.MapPost("/{id:guid}/test-cases", AddTestCaseAsync);
        modules.MapPost("/{id:guid}/test", RunTestAsync);

        app.MapGet("/api/kb/accuracy", AccuracyDashboardAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> ListModulesAsync(AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var modules = await db.KbModules
            .Where(m => m.TenantId == tenantId && m.DeletedAt == null)
            .OrderBy(m => m.Code)
            .Select(m => new KbModuleDto(
                m.Id, m.Code, m.Name, m.Description, m.OwnerRole, m.Status,
                m.Versions.Count,
                m.Versions.Count > 0 ? m.Versions.Max(v => v.Version) : null,
                m.CreatedAt))
            .ToListAsync(ct);
        return Results.Ok(modules);
    }

    private static async Task<IResult> GetModuleAsync(Guid id, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var m = await db.KbModules
            .Where(x => x.Id == id && x.TenantId == tenantId && x.DeletedAt == null)
            .Select(x => new KbModuleDto(
                x.Id, x.Code, x.Name, x.Description, x.OwnerRole, x.Status,
                x.Versions.Count,
                x.Versions.Count > 0 ? x.Versions.Max(v => v.Version) : null,
                x.CreatedAt))
            .FirstOrDefaultAsync(ct);
        return m is null ? Results.NotFound() : Results.Ok(m);
    }

    private static async Task<IResult> CreateModuleAsync(
        CreateKbModuleRequest req,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code) || string.IsNullOrWhiteSpace(req.Name))
            return Results.BadRequest("code_and_name_required");

        var tenantId = tenants.Require().TenantId;
        var duplicate = await db.KbModules.AnyAsync(m => m.TenantId == tenantId && m.Code == req.Code, ct);
        if (duplicate) return Results.Conflict("code_exists");

        var module = KbModule.Create(tenantId, req.Code, req.Name, clock.UtcNow);
        db.Entry(module).Property("Description").CurrentValue = req.Description;
        db.Entry(module).Property("OwnerRole").CurrentValue = req.OwnerRole;
        db.KbModules.Add(module);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/kb/modules/{module.Id}",
            new KbModuleDto(module.Id, module.Code, module.Name, req.Description, req.OwnerRole, module.Status,
                0, null, module.CreatedAt));
    }

    private static async Task<IResult> UpdateModuleAsync(
        Guid id,
        UpdateKbModuleRequest req,
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var module = await db.KbModules.FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId, ct);
        if (module is null || module.DeletedAt is not null) return Results.NotFound();

        var entry = db.Entry(module);
        entry.Property("Name").CurrentValue = req.Name;
        entry.Property("Description").CurrentValue = req.Description;
        entry.Property("OwnerRole").CurrentValue = req.OwnerRole;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ArchiveModuleAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var module = await db.KbModules.FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId, ct);
        if (module is null) return Results.NotFound();
        if (module.DeletedAt is not null) return Results.NoContent();

        db.Entry(module).Property("DeletedAt").CurrentValue = clock.UtcNow;
        db.Entry(module).Property("Status").CurrentValue = "archived";
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListVersionsAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var ok = await db.KbModules.AnyAsync(m => m.Id == id && m.TenantId == tenantId, ct);
        if (!ok) return Results.NotFound();

        var versions = await db.KbVersions
            .Where(v => v.KbModuleId == id)
            .OrderByDescending(v => v.Version)
            .Select(v => new KbVersionDto(v.Id, v.KbModuleId, v.Version, v.Status, v.AccuracyScore, v.DeployedAt, v.CreatedAt))
            .ToListAsync(ct);
        return Results.Ok(versions);
    }

    private static async Task<IResult> CreateVersionAsync(
        Guid id,
        CreateKbVersionRequest req,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ContentMd)) return Results.BadRequest("content_required");

        var tenantId = tenants.Require().TenantId;
        var module = await db.KbModules.FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId, ct);
        if (module is null) return Results.NotFound();

        var nextVersion = await db.KbVersions.Where(v => v.KbModuleId == id).MaxAsync(v => (int?)v.Version, ct) ?? 0;
        var version = KbVersion.Create(id, nextVersion + 1, req.ContentMd, clock.UtcNow);
        db.KbVersions.Add(version);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/kb/modules/{id}/versions/{version.Id}",
            new KbVersionDto(version.Id, version.KbModuleId, version.Version, version.Status,
                version.AccuracyScore, version.DeployedAt, version.CreatedAt));
    }

    private static async Task<IResult> GetVersionDetailAsync(
        Guid id,
        Guid versionId,
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var version = await (
            from v in db.KbVersions
            join m in db.KbModules on v.KbModuleId equals m.Id
            where v.Id == versionId && m.Id == id && m.TenantId == tenantId
            select new KbVersionDetailDto(v.Id, v.KbModuleId, v.Version, v.Status, v.ContentMd,
                v.AccuracyScore, v.DeployedAt, v.CreatedAt)).FirstOrDefaultAsync(ct);
        return version is null ? Results.NotFound() : Results.Ok(version);
    }

    private static async Task<IResult> DeployVersionAsync(
        Guid id,
        Guid versionId,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        KbDeployService deployService,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var owns = await db.KbModules.FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId, ct);
        if (owns is null) return Results.NotFound();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var deployed = await db.KbVersions
            .Where(v => v.KbModuleId == id && v.Status == "deployed")
            .ToListAsync(ct);
        foreach (var prev in deployed) db.Entry(prev).Property("Status").CurrentValue = "archived";

        var target = await db.KbVersions.FirstOrDefaultAsync(v => v.Id == versionId && v.KbModuleId == id, ct);
        if (target is null) return Results.NotFound();
        target.Deploy(clock.UtcNow);
        await db.SaveChangesAsync(ct);

        await deployService.EmbedAndUpsertAsync(target, owns.Code, tenantId, ct);

        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RollbackToVersionAsync(
        Guid id,
        Guid versionId,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        KbDeployService deployService,
        CancellationToken ct) =>
        await DeployVersionAsync(id, versionId, db, tenants, clock, deployService, ct);

    private static async Task<IResult> DiffVersionsAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        int? fromVersion,
        int? toVersion,
        CancellationToken ct)
    {
        if (fromVersion is null || toVersion is null) return Results.BadRequest("from_and_to_required");
        var tenantId = tenants.Require().TenantId;
        var fromV = fromVersion.Value;
        var toV = toVersion.Value;

        var versions = await (
            from v in db.KbVersions
            join m in db.KbModules on v.KbModuleId equals m.Id
            where m.Id == id && m.TenantId == tenantId && (v.Version == fromV || v.Version == toV)
            select v).ToListAsync(ct);

        var src = versions.FirstOrDefault(v => v.Version == fromV);
        var dst = versions.FirstOrDefault(v => v.Version == toV);
        if (src is null || dst is null) return Results.NotFound();

        var diff = UnifiedDiff.Compute(src.ContentMd, dst.ContentMd);
        return Results.Ok(new KbVersionDiff(fromV, toV, diff.Added, diff.Removed, diff.Text));
    }

    private static async Task<IResult> ListTestCasesAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var owns = await db.KbModules.AnyAsync(m => m.Id == id && m.TenantId == tenantId, ct);
        if (!owns) return Results.NotFound();

        var cases = await db.KbTestCases
            .Where(t => t.KbModuleId == id)
            .OrderBy(t => t.CreatedAt)
            .Select(t => new KbTestCaseDto(t.Id, t.Question, t.ExpectedAnswer, t.IsActive))
            .ToListAsync(ct);
        return Results.Ok(cases);
    }

    private static async Task<IResult> AddTestCaseAsync(
        Guid id,
        CreateKbTestCaseRequest req,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Question) || string.IsNullOrWhiteSpace(req.ExpectedAnswer))
            return Results.BadRequest("question_and_answer_required");

        var tenantId = tenants.Require().TenantId;
        var owns = await db.KbModules.AnyAsync(m => m.Id == id && m.TenantId == tenantId, ct);
        if (!owns) return Results.NotFound();

        var test = KbTestCase.Create(id, req.Question, req.ExpectedAnswer, clock.UtcNow);
        db.KbTestCases.Add(test);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/kb/modules/{id}/test-cases/{test.Id}",
            new KbTestCaseDto(test.Id, test.Question, test.ExpectedAnswer, test.IsActive));
    }

    private static async Task<IResult> RunTestAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        IRagRetriever rag,
        IClaudeChatClient claude,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var owns = await db.KbModules.FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId, ct);
        if (owns is null) return Results.NotFound();

        var deployedVersion = await db.KbVersions
            .Where(v => v.KbModuleId == id && v.Status == "deployed")
            .OrderByDescending(v => v.Version)
            .FirstOrDefaultAsync(ct);
        if (deployedVersion is null) return Results.BadRequest("no_deployed_version");

        var cases = await db.KbTestCases
            .Where(t => t.KbModuleId == id && t.IsActive)
            .ToListAsync(ct);
        if (cases.Count == 0) return Results.BadRequest("no_test_cases");

        var results = new List<KbTestCaseResult>();
        foreach (var testCase in cases)
        {
            var chunks = await rag.RetrieveAsync(
                new RagRequest(tenantId, owns.Code, testCase.Question, 3), ct);

            var context = string.Join("\n---\n", chunks.Select(c => c.Snippet));
            var evalPrompt = $"Context:\n{context}\n\nQuestion: {testCase.Question}\n" +
                $"Expected answer: {testCase.ExpectedAnswer}\n\n" +
                "Does the context contain information to answer the question correctly? " +
                "Reply with only JSON: {\"passed\":true/false,\"reason\":\"...\"}";

            var reply = await claude.CompleteAsync(
                "You are a KB accuracy evaluator. Check if the retrieved context supports the expected answer.",
                Array.Empty<ChatTurn>(), evalPrompt, ct);

            var passed = reply.Text.Contains("\"passed\":true", StringComparison.OrdinalIgnoreCase);
            results.Add(new KbTestCaseResult(testCase.Id, testCase.Question, passed, null));
        }

        var passedCount = results.Count(r => r.Passed);
        var score = decimal.Round(100m * passedCount / results.Count, 2);

        deployedVersion.RecordAccuracy(score);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new KbTestRunResult(deployedVersion.Id, deployedVersion.Version,
            results.Count, passedCount, score, results));
    }

    private static async Task<IResult> AccuracyDashboardAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var summaries = await (
            from m in db.KbModules
            where m.TenantId == tenantId && m.DeletedAt == null
            let latest = m.Versions.OrderByDescending(v => v.Version).FirstOrDefault()
            select new KbAccuracySummary(
                m.Id,
                m.Code,
                m.Name,
                latest != null ? latest.Version : (int?)null,
                latest != null ? latest.AccuracyScore : null,
                m.Versions.Where(v => v.AccuracyScore != null).Average(v => (decimal?)v.AccuracyScore),
                m.Versions.Where(v => v.AccuracyScore != null).Max(v => (DateTimeOffset?)v.CreatedAt))
        ).ToListAsync(ct);
        return Results.Ok(summaries);
    }
}

/// <summary>Tiny line-based diff. Good enough for KB version review; not a full Myers diff.</summary>
internal static class UnifiedDiff
{
    public static (int Added, int Removed, string Text) Compute(string from, string to)
    {
        var fromLines = (from ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var toLines = (to ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var fromSet = new HashSet<string>(fromLines, StringComparer.Ordinal);
        var toSet = new HashSet<string>(toLines, StringComparer.Ordinal);

        var sb = new StringBuilder();
        int added = 0, removed = 0;
        foreach (var line in fromLines)
        {
            if (toSet.Contains(line)) sb.Append(' ').Append(line).Append('\n');
            else { sb.Append('-').Append(line).Append('\n'); removed++; }
        }
        foreach (var line in toLines)
        {
            if (!fromSet.Contains(line)) { sb.Append('+').Append(line).Append('\n'); added++; }
        }
        return (added, removed, sb.ToString());
    }
}
