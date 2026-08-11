using System.Globalization;
using System.Text.Json;
using Clawbot.Api.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Api.Services;
using Clawbot.Domain.Analytics;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

/// <summary>
/// Đọc lại kết quả báo cáo do report-agent chốt (bảng + biểu đồ + tải file). Artifact là bất biến:
/// endpoint chỉ trả đúng JSON đã lưu, không tính lại KPI, để link chia sẻ luôn hiện đúng số lúc chạy.
/// </summary>
public static class ReportsEndpoints
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapReports(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/reports")
            .RequirePermission("analytics:read")
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/", ListAsync);
        grp.MapGet("/{id:guid}", GetAsync);
        grp.MapGet("/{id:guid}/export", ExportAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] AppDbContext db,
        [FromServices] ITenantAccessor tenants,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var take = Math.Clamp(limit ?? 20, 1, 100);

        var items = await db.ReportArtifacts.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(take)
            .Select(a => new
            {
                a.Id,
                a.Kind,
                a.Title,
                a.Platform,
                a.Metric,
                a.FromDate,
                a.ToDate,
                a.CreatedAt,
            })
            .ToListAsync(ct).ConfigureAwait(false);

        return Results.Ok(new { total = items.Count, items });
    }

    private static async Task<IResult> GetAsync(
        [FromServices] AppDbContext db,
        [FromServices] ITenantAccessor tenants,
        Guid id,
        CancellationToken ct)
    {
        var artifact = await LoadAsync(db, tenants, id, ct).ConfigureAwait(false);
        if (artifact is null)
            return Results.NotFound(new { error = "report not found" });

        return Results.Ok(new
        {
            artifact.Id,
            artifact.Kind,
            artifact.Title,
            artifact.Platform,
            artifact.Metric,
            artifact.FromDate,
            artifact.ToDate,
            artifact.CreatedAt,
            // Trả thẳng JSON đã lưu: parse rồi serialize lại theo DTO chỉ tổ làm lệch shape với lúc ghi.
            data = JsonSerializer.Deserialize<JsonElement>(artifact.DataJson),
        });
    }

    private static async Task<IResult> ExportAsync(
        [FromServices] AppDbContext db,
        [FromServices] ITenantAccessor tenants,
        Guid id,
        [FromQuery] string? format,
        CancellationToken ct)
    {
        string normalizedFormat;
        try
        {
            normalizedFormat = ReportExportService.NormalizeFormat(format);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        var artifact = await LoadAsync(db, tenants, id, ct).ConfigureAwait(false);
        if (artifact is null)
            return Results.NotFound(new { error = "report not found" });

        var payload = JsonSerializer.Deserialize<ReportArtifactPayload>(artifact.DataJson, PayloadJsonOptions);
        if (payload is null)
            return Results.Problem("report payload is corrupted", statusCode: StatusCodes.Status500InternalServerError);

        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"report-{artifact.Kind}-{artifact.FromDate:yyyyMMdd}.{normalizedFormat}");

        return Results.File(
            ReportExportService.Build(normalizedFormat, artifact.Title, payload),
            ReportExportService.ContentTypeFor(normalizedFormat),
            fileName);
    }

    private static Task<ReportArtifact?> LoadAsync(
        AppDbContext db, ITenantAccessor tenants, Guid id, CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        return db.ReportArtifacts.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId, ct);
    }
}
