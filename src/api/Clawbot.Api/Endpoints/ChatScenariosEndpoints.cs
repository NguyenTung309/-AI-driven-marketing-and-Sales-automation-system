using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.ChatScenarios;
using Clawbot.Domain.ChatScenarios;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

// M05 — chat_scenarios CRUD + trigger/platform match + success-rate tracker.
// Rows are tenant-scoped; AppDbContext applies the global ITenantOwned query filter,
// so handlers do not filter TenantId explicitly on reads.
public static class ChatScenariosEndpoints
{
    public static IEndpointRouteBuilder MapChatScenarios(this IEndpointRouteBuilder app)
    {
        // SPEC-11 §6a: reads (incl. read-only match) need chat-scenarios:read; mutations write.
        var grp = app.MapGroup("/api/chat-scenarios");

        grp.MapGet("/", ListAsync).RequirePermission("chat-scenarios:read");
        grp.MapGet("/{id:guid}", GetAsync).RequirePermission("chat-scenarios:read");
        grp.MapPost("/", CreateAsync).RequirePermission("chat-scenarios:write");
        grp.MapPut("/{id:guid}", UpdateAsync).RequirePermission("chat-scenarios:write");
        grp.MapDelete("/{id:guid}", DeleteAsync).RequirePermission("chat-scenarios:write");
        grp.MapPost("/match", MatchAsync).RequirePermission("chat-scenarios:read");
        grp.MapPost("/{id:guid}/outcome", RecordOutcomeAsync).RequirePermission("chat-scenarios:write");

        return app;
    }

    private static ChatScenarioDto ToDto(ChatScenario s) =>
        new(s.Id, s.Code, s.GroupName, s.TriggerText, s.ResponseTemplate, s.ToneVoice, s.Platforms, s.SuccessRate);

    private static async Task<IResult> ListAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct,
        string? group = null,
        string? platform = null)
    {
        _ = tenants.Require();
        var query = db.ChatScenarios.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(group))
            query = query.Where(s => s.GroupName == group);
        if (!string.IsNullOrWhiteSpace(platform))
            query = query.Where(s => s.Platforms.Contains(platform));

        var items = await query
            .OrderBy(s => s.Code)
            .Select(s => new ChatScenarioDto(
                s.Id, s.Code, s.GroupName, s.TriggerText, s.ResponseTemplate, s.ToneVoice, s.Platforms, s.SuccessRate))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(items);
    }

    private static async Task<IResult> GetAsync(
        Guid id, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        _ = tenants.Require();
        var s = await db.ChatScenarios.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        return s is null ? Results.NotFound() : Results.Ok(ToDto(s));
    }

    private static async Task<IResult> CreateAsync(
        CreateChatScenarioRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        if (string.IsNullOrWhiteSpace(body.Code)
            || string.IsNullOrWhiteSpace(body.GroupName)
            || string.IsNullOrWhiteSpace(body.TriggerText)
            || string.IsNullOrWhiteSpace(body.ResponseTemplate))
            return Results.BadRequest(new { error = "code, groupName, triggerText, responseTemplate required" });

        var exists = await db.ChatScenarios.AnyAsync(s => s.Code == body.Code, ct).ConfigureAwait(false);
        if (exists) return Results.Conflict(new { error = "code already exists" });

        var scenario = ChatScenario.Create(
            tenant.TenantId,
            body.Code,
            body.GroupName,
            body.TriggerText,
            body.ResponseTemplate,
            body.Platforms ?? string.Empty,
            clock.UtcNow);

        if (!string.IsNullOrWhiteSpace(body.ToneVoice))
            scenario.Update(body.GroupName, body.TriggerText, body.ResponseTemplate, body.Platforms ?? string.Empty, body.ToneVoice, clock.UtcNow);

        db.ChatScenarios.Add(scenario);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Created($"/api/chat-scenarios/{scenario.Id}", ToDto(scenario));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateChatScenarioRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var scenario = await db.ChatScenarios.FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false);
        if (scenario is null) return Results.NotFound();

        scenario.Update(body.GroupName, body.TriggerText, body.ResponseTemplate, body.Platforms ?? string.Empty, body.ToneVoice, clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToDto(scenario));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        _ = tenants.Require();
        var scenario = await db.ChatScenarios.FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false);
        if (scenario is null) return Results.NotFound();
        db.ChatScenarios.Remove(scenario);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> MatchAsync(
        MatchScenarioRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        _ = tenants.Require();
        if (string.IsNullOrWhiteSpace(body.Text))
            return Results.BadRequest(new { error = "text required" });

        var candidates = await db.ChatScenarios.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var match = ChatScenarioMatcher.Match(body.Text, body.Platform, candidates);
        return Results.Ok(new MatchScenarioResponse(match is null ? null : ToDto(match)));
    }

    private static async Task<IResult> RecordOutcomeAsync(
        Guid id,
        RecordOutcomeRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var scenario = await db.ChatScenarios.FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false);
        if (scenario is null) return Results.NotFound();
        scenario.RecordOutcome(body.Converted, clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToDto(scenario));
    }
}
