using Clawbot.Api.Contracts.Experiments;
using Clawbot.Api.Middleware;
using Clawbot.Api.Services;
using Clawbot.Domain.Experiments;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class ExperimentsEndpoints
{
    public static IEndpointRouteBuilder MapExperiments(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/experiments")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        group.MapGet("/", ListAsync);
        group.MapPost("/", CreateAsync);
        group.MapGet("/{id:guid}/summary", SummaryAsync);
        group.MapPost("/{id:guid}/assign", AssignAsync);
        group.MapPost("/{id:guid}/events", RecordEventAsync);
        group.MapPost("/{id:guid}/stop", StopAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        string? targetType,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var query = db.Experiments
            .AsNoTracking()
            .Include(e => e.Variants)
            .Where(e => e.TenantId == tenantId && e.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(targetType))
        {
            var normalized = targetType.Trim().ToLowerInvariant();
            query = query.Where(e => e.TargetType == normalized);
        }

        var rows = await query
            .OrderBy(e => e.Code)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Results.Ok(rows.Select(ToDto));
    }

    private static async Task<IResult> CreateAsync(
        CreateExperimentRequest request,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code)) return Results.BadRequest("code_required");
        if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest("name_required");
        var targetType = NormalizeTargetType(request.TargetType);
        if (targetType is null) return Results.BadRequest("target_type_invalid");
        if (request.TargetId == Guid.Empty) return Results.BadRequest("target_id_required");
        if (request.Variants.Count < 2) return Results.BadRequest("variants_min_2");
        if (request.Variants.Any(v => string.IsNullOrWhiteSpace(v.Code) || string.IsNullOrWhiteSpace(v.Name) || v.Weight <= 0))
            return Results.BadRequest("variant_invalid");

        var tenantId = tenants.Require().TenantId;
        var duplicate = await db.Experiments.AnyAsync(e => e.TenantId == tenantId && e.Code == request.Code.Trim(), ct)
            .ConfigureAwait(false);
        if (duplicate) return Results.Conflict("experiment_exists");

        var experiment = Experiment.Create(tenantId, request.Code, targetType, request.TargetId, request.Name, clock.UtcNow);
        foreach (var variant in request.Variants)
        {
            experiment.AddVariant(
                variant.Code,
                variant.Name,
                variant.Weight,
                variant.ChatScenarioId,
                variant.KbVersionId,
                clock.UtcNow);
        }

        db.Experiments.Add(experiment);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Created($"/api/experiments/{experiment.Id}", ToDto(experiment));
    }

    private static async Task<IResult> AssignAsync(
        Guid id,
        AssignExperimentRequest request,
        ITenantAccessor tenants,
        ExperimentService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.SubjectKey)) return Results.BadRequest("subject_key_required");
        var result = await service.AssignAsync(tenants.Require().TenantId, id, request.SubjectKey, ct).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> RecordEventAsync(
        Guid id,
        RecordExperimentEventRequest request,
        ITenantAccessor tenants,
        ExperimentService service,
        CancellationToken ct)
    {
        if (request.VariantId == Guid.Empty) return Results.BadRequest("variant_id_required");
        if (string.IsNullOrWhiteSpace(request.SubjectKey)) return Results.BadRequest("subject_key_required");
        if (string.IsNullOrWhiteSpace(request.EventType)) return Results.BadRequest("event_type_required");

        await service.RecordEventAsync(
            tenants.Require().TenantId,
            id,
            request.VariantId,
            request.SubjectKey,
            request.EventType,
            request.Value,
            ct).ConfigureAwait(false);

        return Results.NoContent();
    }

    private static async Task<IResult> SummaryAsync(
        Guid id,
        ITenantAccessor tenants,
        ExperimentService service,
        CancellationToken ct)
    {
        var summary = await service.GetSummaryAsync(tenants.Require().TenantId, id, ct).ConfigureAwait(false);
        return Results.Ok(summary);
    }

    private static async Task<IResult> StopAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var experiment = await db.Experiments.FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId, ct)
            .ConfigureAwait(false);
        if (experiment is null) return Results.NotFound();

        experiment.Stop(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToDto(experiment));
    }

    private static ExperimentDto ToDto(Experiment experiment) =>
        new(
            experiment.Id,
            experiment.Code,
            experiment.Name,
            experiment.TargetType,
            experiment.TargetId,
            experiment.Status,
            experiment.Variants
                .OrderBy(v => v.Code, StringComparer.Ordinal)
                .Select(v => new ExperimentVariantDto(
                    v.Id,
                    v.Code,
                    v.Name,
                    v.Weight,
                    v.ChatScenarioId,
                    v.KbVersionId))
                .ToList());

    private static string? NormalizeTargetType(string targetType)
    {
        var normalized = targetType.Trim().ToLowerInvariant();
        return normalized is "chat_scenario" or "kb_version" ? normalized : null;
    }
}
