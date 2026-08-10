using System.IO.Compression;
using System.Text;
using Microsoft.Net.Http.Headers;
using Clawbot.Api.Auth;
using Clawbot.Api.Jobs;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Docs;
using Clawbot.Agents.Core.Kb;
using Clawbot.Api.Contracts.KnowledgeBase;
using Clawbot.Api.Middleware;
using Clawbot.Api.Services;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class KbEndpoints
{
    private static readonly Action<ILogger, Guid, Exception> LogUploadFailed =
        LoggerMessage.Define<Guid>(LogLevel.Error, new EventId(7101, "KbUploadExtractionFailed"),
            "KB upload extraction failed for module {ModuleId}");

    public static IEndpointRouteBuilder MapKb(this IEndpointRouteBuilder app)
    {
        var modules = app.MapGroup("/api/kb/modules").RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        modules.MapGet("/", ListModulesAsync).RequirePermission("kb:read");
        modules.MapGet("/{id:guid}", GetModuleAsync).RequirePermission("kb:read");
        modules.MapPost("/", CreateModuleAsync).RequirePermission("kb:write");
        modules.MapPut("/{id:guid}", UpdateModuleAsync).RequirePermission("kb:write");
        modules.MapPost("/{id:guid}/archive", ArchiveModuleAsync).RequirePermission("kb:write");

        modules.MapGet("/{id:guid}/versions", ListVersionsAsync).RequirePermission("kb:read");
        modules.MapPost("/{id:guid}/versions", CreateVersionAsync).RequirePermission("kb:write");
        // DisableAntiforgery: JWT bearer auth carries no cookie credential, so CSRF doesn't apply.
        modules.MapPost("/{id:guid}/upload", UploadVersionAsync)
            .RequirePermission("kb:write")
            .RequireRateLimiting(RateLimitingExtensions.UploadPolicy)
            .DisableAntiforgery();
        modules.MapGet("/{id:guid}/versions/{versionId:guid}", GetVersionDetailAsync).RequirePermission("kb:read");
        modules.MapDelete("/{id:guid}/versions/{versionId:guid}", DeleteVersionAsync).RequirePermission("kb:write");
        modules.MapPost("/{id:guid}/versions/{versionId:guid}/deploy", DeployVersionAsync).RequirePermission("kb:write");
        modules.MapPost("/{id:guid}/versions/{versionId:guid}/rollback", RollbackToVersionAsync).RequirePermission("kb:write");
        modules.MapGet("/{id:guid}/diff", DiffVersionsAsync).RequirePermission("kb:read");

        modules.MapGet("/{id:guid}/test-cases", ListTestCasesAsync).RequirePermission("kb:read");
        modules.MapPost("/{id:guid}/test-cases", AddTestCaseAsync).RequirePermission("kb:write");
        modules.MapPost("/{id:guid}/test-cases/generate", GenerateTestCasesAsync).RequirePermission("kb:write");
        modules.MapPost("/{id:guid}/test", RunTestAsync).RequirePermission("kb:write");

        app.MapGet("/api/kb/accuracy", AccuracyDashboardAsync).RequirePermission("kb:read");
        // DisableAntiforgery: JWT bearer auth carries no cookie credential, so CSRF doesn't apply.
        app.MapPost("/api/kb/classify-upload", ClassifyUploadAsync)
            .RequirePermission("kb:write")
            .RequireRateLimiting(RateLimitingExtensions.UploadPolicy)
            .DisableAntiforgery();
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

    private const long MaxUploadBytes = 10 * 1024 * 1024;

    private static async Task<string?> ResolveUploadFileNameAsync(IFormFile file, IDocumentTextExtractor extractor, CancellationToken ct)
    {
        // Browsers/proxies sometimes post Blob metadata as FileName="blob" and ContentType="application/octet-stream".
        // Prefer metadata names first; if all fail, sniff bytes/package entries so real .docx/.xlsx uploads don't die at
        // unsupported_format before the markdown extractor gets a chance to run.
        var candidates = new[]
        {
            file.FileName,
            SafeContentDispositionFileName(file.ContentDisposition),
            SafeContentDispositionFileNameStar(file.ContentDisposition),
            FileNameFromContentType(file.ContentType),
        };

        var metadataName = candidates.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name) && extractor.CanExtract(name));
        if (metadataName is not null)
            return metadataName;

        await using var stream = file.OpenReadStream();
        return await FileNameFromMagicBytesAsync(stream, ct).ConfigureAwait(false);
    }

    private static string? SafeContentDispositionFileName(string? contentDisposition)
    {
        if (string.IsNullOrWhiteSpace(contentDisposition)) return null;
        return ContentDispositionHeaderValue.TryParse(contentDisposition, out var value) ? value.FileName.Value : null;
    }

    private static string? SafeContentDispositionFileNameStar(string? contentDisposition)
    {
        if (string.IsNullOrWhiteSpace(contentDisposition)) return null;
        return ContentDispositionHeaderValue.TryParse(contentDisposition, out var value) ? value.FileNameStar.Value : null;
    }

    private static string? FileNameFromContentType(string? contentType) => contentType?.ToLowerInvariant() switch
    {
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "upload.docx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "upload.xlsx",
        "application/pdf" => "upload.pdf",
        "text/csv" => "upload.csv",
        "text/markdown" => "upload.md",
        "text/plain" => "upload.txt",
        _ => null,
    };

    private static async Task<string?> FileNameFromMagicBytesAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        if (buffer.Length == 0) return null;

        var head = buffer.GetBuffer();
        var len = (int)Math.Min(4, buffer.Length);
        if (len >= 4 && head[0] == 0x25 && head[1] == 0x50 && head[2] == 0x44 && head[3] == 0x46)
            return "upload.pdf";

        if (len >= 2 && head[0] == 0x50 && head[1] == 0x4B)
            return OfficeFileNameFromZip(buffer);

        return LooksLikeText(buffer) ? "upload.txt" : null;
    }

    private static string? OfficeFileNameFromZip(MemoryStream buffer)
    {
        try
        {
            buffer.Position = 0;
            using var zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.GetEntry("word/document.xml") is not null) return "upload.docx";
            if (zip.GetEntry("xl/workbook.xml") is not null) return "upload.xlsx";
        }
        catch (InvalidDataException)
        {
            return null;
        }
        return null;
    }

    private static bool LooksLikeText(MemoryStream buffer)
    {
        var sample = buffer.GetBuffer().AsSpan(0, (int)Math.Min(512, buffer.Length));
        return sample.IndexOf((byte)0) < 0;
    }

    // Upload a file (docx/xlsx/csv/pdf/txt/md) → auto-convert to markdown → save as a DRAFT version.
    // Deliberately does NOT deploy: the operator reviews/edits the converted text, then deploys.
    private static async Task<IResult> UploadVersionAsync(
        Guid id,
        IFormFile file,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        IDocumentTextExtractor extractor,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0) return Results.BadRequest("file_required");
        if (file.Length > MaxUploadBytes) return Results.BadRequest("file_too_large");
        var uploadFileName = await ResolveUploadFileNameAsync(file, extractor, ct).ConfigureAwait(false);
        if (uploadFileName is null)
            return Results.BadRequest("unsupported_format");

        var tenantId = tenants.Require().TenantId;
        var module = await db.KbModules.FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId, ct);
        if (module is null) return Results.NotFound();

        ExtractedDocument extracted;
        try
        {
            await using var stream = file.OpenReadStream();
            extracted = await extractor.ExtractAsync(stream, uploadFileName, ct);
        }
        catch (DocumentExtractionException ex)
        {
            return Results.BadRequest(new { error = "extraction_failed", message = ex.Message });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Don't leak parser internals (paths, stack) to the client.
            LogUploadFailed(loggerFactory.CreateLogger("KbUpload"), id, ex);
            return Results.BadRequest(new { error = "extraction_failed", message = "Không đọc được nội dung tệp." });
        }

        var nextVersion = await db.KbVersions.Where(v => v.KbModuleId == id).MaxAsync(v => (int?)v.Version, ct) ?? 0;
        var version = KbVersion.Create(id, nextVersion + 1, extracted.Markdown, clock.UtcNow);
        db.KbVersions.Add(version);
        await db.SaveChangesAsync(ct);

        var dto = new KbVersionDto(version.Id, version.KbModuleId, version.Version, version.Status,
            version.AccuracyScore, version.DeployedAt, version.CreatedAt);
        return Results.Created($"/api/kb/modules/{id}/versions/{version.Id}",
            new KbUploadResult(dto, extracted.SourceFormat, extracted.CharCount, extracted.Markdown));
    }

    private const int MaxClassifyFiles = 20;

    // Mã lỗi per-file khi nạp tự động: file đã lưu bản nháp nhưng bước deploy/embed thất bại.
    // Job nạp file đọc mã này để nêu rõ trong summary (Success vẫn true vì nội dung đã vào kho).
    internal const string DeployFailedError = "deploy_failed";

    // Bulk upload: extract each file, let the LLM pick (or create) the KB module, save a version.
    // autoDeploy=true (default) also deploys + embeds; failures are per-file, one bad file
    // doesn't block the rest.
    // Phân loại + nạp KB từ file tải lên: LLM đọc từng file rồi chọn/ tạo module, deploy + embed.
    // File KHÔNG nhét vào payload job (dữ liệu khách thô) — đẩy lên object storage (MinIO/local),
    // payload chỉ mang key. Job đọc lại file từ storage.
    private static async Task<IResult> ClassifyUploadAsync(
        IFormFileCollection files,
        bool? autoDeploy,
        bool? autoTest,
        ITenantAccessor tenants,
        IDocumentTextExtractor extractor,
        IDocumentStorage storage,
        IJobLauncher jobs,
        HttpContext http,
        CancellationToken ct)
    {
        if (files is null || files.Count == 0) return Results.BadRequest("file_required");
        if (files.Count > MaxClassifyFiles) return Results.BadRequest("too_many_files");

        var tenantId = tenants.Require().TenantId;
        var staged = new List<KbStagedUpload>(files.Count);
        foreach (var file in files)
        {
            if (file.Length == 0) return Results.BadRequest("file_required");
            if (file.Length > MaxUploadBytes) return Results.BadRequest("file_too_large");

            var extractName = await ResolveUploadFileNameAsync(file, extractor, ct).ConfigureAwait(false);
            if (extractName is null) return Results.BadRequest("unsupported_format");

            using var buffer = new MemoryStream();
            await using (var source = file.OpenReadStream())
            {
                await source.CopyToAsync(buffer, ct).ConfigureAwait(false);
            }

            var key = $"kb-uploads/{tenantId:D}/{Guid.NewGuid():N}{Path.GetExtension(extractName)}";
            await storage.SaveAsync(buffer.ToArray(), key, file.ContentType, ct).ConfigureAwait(false);

            var displayName = string.IsNullOrWhiteSpace(file.FileName) ? "(không tên)" : file.FileName;
            staged.Add(new KbStagedUpload(key, displayName, extractName));
        }

        var jobId = await jobs.LaunchAsync(
            KbClassifyUploadJobHandler.JobType,
            $"Phân loại và nạp {staged.Count} tệp vào kho tri thức",
            new KbClassifyUploadJobPayload(staged, autoDeploy ?? true, autoTest ?? true),
            CurrentUserId(http),
            ct: ct).ConfigureAwait(false);

        return Results.Accepted($"/api/jobs/{jobId}", new { jobId, statusUrl = $"/agents?job={jobId}" });
    }

    internal static async Task<KbClassifiedFileDto> ClassifyOneAsync(
        byte[] content,
        string fileName,
        string uploadFileName,
        bool deploy,
        Guid tenantId,
        List<KbModule> modules,
        AppDbContext db,
        IClock clock,
        IDocumentTextExtractor extractor,
        KbAutoClassifyService classifier,
        KbDeployService deployService,
        ILogger logger,
        CancellationToken ct)
    {
        static KbClassifiedFileDto Fail(string name, string error) =>
            new(name, false, error, null, null, null, false, 0d, null, null, false);

        ExtractedDocument extracted;
        try
        {
            using var stream = new MemoryStream(content);
            extracted = await extractor.ExtractAsync(stream, uploadFileName, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUploadFailed(logger, Guid.Empty, ex);
            return Fail(fileName, "extraction_failed");
        }

        KbClassificationVerdict? verdict;
        try
        {
            var choices = modules
                .Select(m => new KbModuleChoice(m.Code, m.Name, m.Description))
                .ToList();
            verdict = await classifier.ClassifyAsync(tenantId, fileName, extracted.Markdown, choices, ct).ConfigureAwait(false);
        }
        catch (LlmConfigNotConfiguredException)
        {
            return Fail(fileName, "llm_not_configured");
        }
        if (verdict is null) return Fail(fileName, "classification_failed");

        var (module, isNew) = ResolveTargetModule(verdict, modules, tenantId, clock, db);
        if (module is null) return Fail(fileName, "classification_failed");
        if (isNew) modules.Add(module);

        var nextVersion = await db.KbVersions.Where(v => v.KbModuleId == module.Id).MaxAsync(v => (int?)v.Version, ct) ?? 0;
        var version = KbVersion.Create(module.Id, nextVersion + 1, extracted.Markdown, clock.UtcNow);
        db.KbVersions.Add(version);
        await db.SaveChangesAsync(ct);

        var deployed = false;
        string? error = null;
        if (deploy)
        {
            try
            {
                // Embed lên vector store TRƯỚC, thành công mới archive bản cũ + đánh dấu deployed.
                // Thứ tự ngược lại tạo "bản deployed ma": embed lỗi nhưng Status đã mutate trên entity
                // đang tracking, SaveChanges kế tiếp trong cùng scope flush xuống DB → kiểm thử chấm 0%
                // vì Qdrant không có chunk nào của bản này.
                await deployService.EmbedAndUpsertAsync(version, module.Code, tenantId, ct);

                var previous = await db.KbVersions
                    .Where(v => v.KbModuleId == module.Id && v.Status == "deployed")
                    .ToListAsync(ct);
                foreach (var prev in previous) db.Entry(prev).Property("Status").CurrentValue = "archived";
                version.Deploy(clock.UtcNow);
                await db.SaveChangesAsync(ct);
                deployed = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogUploadFailed(logger, module.Id, ex);
                error = DeployFailedError;
            }
        }

        return new KbClassifiedFileDto(
            fileName, true, error, module.Id, module.Code, module.Name, isNew,
            verdict.Confidence, verdict.Reason,
            new KbVersionDto(version.Id, version.KbModuleId, version.Version, version.Status,
                version.AccuracyScore, version.DeployedAt, version.CreatedAt),
            deployed);
    }

    private static (KbModule? Module, bool IsNew) ResolveTargetModule(
        KbClassificationVerdict verdict,
        List<KbModule> modules,
        Guid tenantId,
        IClock clock,
        AppDbContext db)
    {
        if (verdict.ModuleCode is not null)
        {
            var match = modules.FirstOrDefault(m => string.Equals(m.Code, verdict.ModuleCode, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return (match, false);
        }

        var name = verdict.NewName ?? verdict.NewCode ?? verdict.ModuleCode;
        if (name is null) return (null, false);

        var code = SlugifyModuleCode(verdict.NewCode ?? name);
        var existing = modules.FirstOrDefault(m => string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return (existing, false);

        var module = KbModule.Create(tenantId, code, name, clock.UtcNow);
        if (!string.IsNullOrWhiteSpace(verdict.NewDescription))
            db.Entry(module).Property("Description").CurrentValue = verdict.NewDescription;
        db.KbModules.Add(module);
        return (module, true);
    }

    internal static string SlugifyModuleCode(string raw)
    {
        var slug = new string(raw.Trim().ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')
            .ToArray()).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return string.IsNullOrEmpty(slug) ? $"kb-{Guid.NewGuid():N}"[..11] : slug;
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

    private static async Task<IResult> DeleteVersionAsync(
        Guid id,
        Guid versionId,
        AppDbContext db,
        ITenantAccessor tenants,
        KbDeployService deployService,
        bool includeRollbackTarget,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var module = await db.KbModules
            .FirstOrDefaultAsync(item => item.Id == id && item.TenantId == tenantId && item.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (module is null) return Results.NotFound();

        var version = await db.KbVersions
            .FirstOrDefaultAsync(item => item.Id == versionId && item.KbModuleId == id, ct)
            .ConfigureAwait(false);
        if (version is null) return Results.NotFound();
        if (string.Equals(version.Status, "deployed", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Conflict(new
            {
                errorCode = "kb.version_deployed_not_deletable",
                message = "Không xóa được bản đang phát hành.",
            });
        }

        var usedByExperiment = await db.ExperimentVariants
            .AnyAsync(item => item.KbVersionId == versionId, ct)
            .ConfigureAwait(false);
        if (usedByExperiment)
        {
            return Results.Conflict(new
            {
                errorCode = "kb.version_in_experiment",
                message = "Không xóa được bản đang được dùng trong thí nghiệm.",
            });
        }

        var rollbackTargetId = await db.KbVersions
            .Where(item => item.KbModuleId == id && item.Status == "archived")
            .OrderByDescending(item => item.Version)
            .Select(item => (Guid?)item.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (!includeRollbackTarget && rollbackTargetId == version.Id)
        {
            return Results.Conflict(new
            {
                errorCode = "kb.rollback_target_not_deletable",
                message = "Không xóa được bản lưu gần nhất để khôi phục. Hãy giữ lại bản này hoặc xác nhận xóa bản khôi phục.",
            });
        }

        await deployService.DeleteVectorsAsync(version, tenantId, ct).ConfigureAwait(false);
        db.KbVersions.Remove(version);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    // Phát hành chạy ngầm qua job platform (KbDeployJobHandler): KB lớn + embedding thật là hàng chục
    // lời gọi API — giữ HTTP request là treo nút UI vài phút. Job tự thông báo khi xong/lỗi.
    private static async Task<IResult> DeployVersionAsync(
        Guid id,
        Guid versionId,
        AppDbContext db,
        ITenantAccessor tenants,
        IJobLauncher jobs,
        HttpContext http,
        CancellationToken ct) =>
        await LaunchDeployJobAsync(id, versionId, isRollback: false, db, tenants, jobs, http, ct);

    private static async Task<IResult> RollbackToVersionAsync(
        Guid id,
        Guid versionId,
        AppDbContext db,
        ITenantAccessor tenants,
        IJobLauncher jobs,
        HttpContext http,
        CancellationToken ct) =>
        await LaunchDeployJobAsync(id, versionId, isRollback: true, db, tenants, jobs, http, ct);

    private static async Task<IResult> LaunchDeployJobAsync(
        Guid id,
        Guid versionId,
        bool isRollback,
        AppDbContext db,
        ITenantAccessor tenants,
        IJobLauncher jobs,
        HttpContext http,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var owns = await db.KbModules.FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId, ct);
        if (owns is null) return Results.NotFound();

        var version = await db.KbVersions
            .Where(v => v.Id == versionId && v.KbModuleId == id)
            .Select(v => new { v.Version })
            .FirstOrDefaultAsync(ct);
        if (version is null) return Results.NotFound();

        var title = isRollback
            ? $"Khôi phục KB {owns.Code} về v{version.Version}"
            : $"Phát hành KB {owns.Code} v{version.Version}";
        var jobId = await jobs.LaunchAsync(
            KbDeployJobHandler.JobType,
            title,
            new KbDeployJobPayload(id, versionId, isRollback),
            CurrentUserId(http),
            ct: ct).ConfigureAwait(false);

        return Results.Accepted($"/api/jobs/{jobId}", new { jobId, statusUrl = $"/agents?job={jobId}" });
    }

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

    // Auto-author Q&A test cases from the latest KB content (draft or deployed) so operators don't
    // have to hand-write the whole accuracy suite. Skips questions already present (case-insensitive).
    // Sinh test case bằng LLM — chạy ngầm; điều kiện (module + có nội dung) kiểm ngay.
    private static async Task<IResult> GenerateTestCasesAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        IJobLauncher jobs,
        GenerateKbTestCasesRequest? req,
        HttpContext http,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var module = await db.KbModules.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId, ct);
        if (module is null) return Results.NotFound();

        var hasContent = await db.KbVersions.AnyAsync(v => v.KbModuleId == id, ct);
        if (!hasContent) return Results.BadRequest("no_content");

        // Không truyền count -> null -> job tự tính số case theo độ dài tài liệu (phủ tối đa).
        // Có count -> tôn trọng lựa chọn người dùng, kẹp trong [1, ManualMaxCases].
        int? count = req?.Count is > 0
            ? Math.Clamp(req.Count.Value, 1, KbTestingOrchestrator.ManualMaxCases)
            : null;
        var title = count is int c
            ? $"Sinh {c} test case cho KB {module.Code}"
            : $"Sinh test case cho KB {module.Code} (tự động phủ theo tài liệu)";
        var jobId = await jobs.LaunchAsync(
            KbGenerateTestCasesJobHandler.JobType,
            title,
            new KbGenerateTestCasesJobPayload(id, count),
            CurrentUserId(http),
            ct: ct).ConfigureAwait(false);

        return Results.Accepted($"/api/jobs/{jobId}", new { jobId, statusUrl = $"/agents?job={jobId}" });
    }

    // Chạy test KB ngầm: mỗi case là 1 lượt hỏi agent, chục case là vài phút — không giữ HTTP request.
    // Điều kiện chạy (module tồn tại, có bản deployed, có case) kiểm ngay để lỗi trả về liền.
    private static async Task<IResult> RunTestAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        IJobLauncher jobs,
        HttpContext http,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var owns = await db.KbModules.FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenantId, ct);
        if (owns is null) return Results.NotFound();

        var hasDeployedVersion = await db.KbVersions
            .AnyAsync(v => v.KbModuleId == id && v.Status == "deployed", ct);
        if (!hasDeployedVersion) return Results.BadRequest("no_deployed_version");

        var hasCases = await db.KbTestCases.AnyAsync(t => t.KbModuleId == id && t.IsActive, ct);
        if (!hasCases) return Results.BadRequest("no_test_cases");

        var jobId = await jobs.LaunchAsync(
            KbTestJobHandler.JobType,
            $"Chạy test KB: {owns.Code}",
            new KbTestJobPayload(id),
            CurrentUserId(http),
            ct: ct).ConfigureAwait(false);

        return Results.Accepted($"/api/jobs/{jobId}", new { jobId, statusUrl = $"/agents?job={jobId}" });
    }

    private static Guid? CurrentUserId(HttpContext http) =>
        Guid.TryParse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)
        && id != Guid.Empty
            ? id
            : null;

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



