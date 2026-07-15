using Clawbot.Api.Common.Pagination;
using Clawbot.Api.Middleware;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public sealed record TaskRunStatsResponse(
    int TotalSessions,
    int RunningSessions,
    int CompletedSessions,
    int TraceEvents,
    int AuditEvents,
    int TokensLast30Days);

public sealed record TaskRunListItemResponse(
    Guid Id,
    string? AgentCode,
    string AgentName,
    string AgentType,
    string Goal,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    double DurationMs,
    int TraceCount,
    string? LastPhase,
    string? LastMessage,
    int TotalTokens,
    decimal Usd);

public sealed record TaskRunTraceResponse(
    Guid Id,
    Guid SessionId,
    string? TaskId,
    string AgentName,
    string Phase,
    string Message,
    DateTimeOffset OccurredAt);

public sealed record TaskRunAuditResponse(
    Guid Id,
    string Action,
    string ResourceType,
    Guid? ResourceId,
    string? DiffJson,
    string? IpAddress,
    string? UserAgent,
    DateTimeOffset OccurredAt);

public sealed record TaskRunListResponse(
    int Total,
    int Page,
    int PageSize,
    TaskRunStatsResponse Stats,
    IReadOnlyList<TaskRunListItemResponse> Items);

/// <summary>Cursor envelope for task-run feed (keeps stats on every page for UI chips).</summary>
public sealed record TaskRunCursorPage(
    IReadOnlyList<TaskRunListItemResponse> Items,
    string? NextCursor,
    int? Total,
    TaskRunStatsResponse Stats);

public sealed record TaskRunDetailResponse(
    TaskRunListItemResponse Run,
    IReadOnlyList<TaskRunTraceResponse> Traces,
    IReadOnlyList<TaskRunAuditResponse> AuditEvents);

public sealed record AuditLogListResponse(
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<TaskRunAuditResponse> Items);

public sealed record AuditLogCursorPage(
    IReadOnlyList<TaskRunAuditResponse> Items,
    string? NextCursor,
    int? Total);

public static class LogsEndpoints
{
    public static IEndpointRouteBuilder MapLogs(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/logs")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        group.MapGet("/task-runs", ListTaskRunsAsync);
        group.MapGet("/task-runs/{sessionId:guid}", GetTaskRunAsync);
        group.MapGet("/audit", ListAuditAsync);

        return group;
    }

    private static async Task<IResult> ListTaskRunsAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        [FromQuery] string? agentCode,
        [FromQuery] string? status,
        [FromQuery] string? q,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var tenantId = tenants.Require().TenantId;
        if (pageSize is < 1 or > 100) pageSize = 25;

        var agents = await LoadAgentsAsync(db, ct);
        var query = db.AgentSessions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(agentCode))
        {
            var agent = agents.FirstOrDefault(a => string.Equals(a.Code, agentCode.Trim(), StringComparison.OrdinalIgnoreCase));
            query = agent is null ? query.Where(_ => false) : query.Where(session => session.AgentId == agent.Id);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim().ToLowerInvariant();
            query = query.Where(session => session.Status == normalized);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var search = q.Trim();
            var searchPattern = $"%{EscapeLikePattern(search)}%";
            var matchingAgentIds = agents
                .Where(agent =>
                    agent.Code.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    agent.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    agent.AgentType.Contains(search, StringComparison.OrdinalIgnoreCase))
                .Select(agent => agent.Id)
                .ToArray();

            query = query.Where(session =>
                (session.Goal != null && EF.Functions.Like(session.Goal, searchPattern, "\\")) ||
                (session.AgentId.HasValue && matchingAgentIds.Contains(session.AgentId.Value)));
        }

        var key = KeysetQuery.Decode(cursor);
        int? total = key is null ? await query.CountAsync(ct) : null;
        if (key is not null)
        {
            var ts = key.Value.Ts;
            var id = key.Value.Id;
            query = query.Where(session =>
                session.StartedAt < ts || (session.StartedAt == ts && session.Id < id));
        }

        var fetched = await query
            .OrderByDescending(session => session.StartedAt)
            .ThenByDescending(session => session.Id)
            .Take(pageSize + 1)
            .Select(session => new SessionRow(
                session.Id,
                session.AgentId,
                session.Goal,
                session.Status,
                session.StartedAt,
                session.FinishedAt))
            .ToListAsync(ct);

        var (sessions, nextCursor) = KeysetQuery.SliceWithCursor(fetched, pageSize, s => s.StartedAt, s => s.Id);
        var items = await BuildRunItemsAsync(db, tenantId, agents, sessions, clock.UtcNow, ct);
        var stats = await BuildStatsAsync(db, tenantId, clock.UtcNow, ct);

        return Results.Ok(new TaskRunCursorPage(items, nextCursor, total, stats));
    }

    private static async Task<IResult> GetTaskRunAsync(
        Guid sessionId,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct = default)
    {
        var tenantId = tenants.Require().TenantId;
        var session = await db.AgentSessions
            .AsNoTracking()
            .Where(item => item.Id == sessionId)
            .Select(item => new SessionRow(
                item.Id,
                item.AgentId,
                item.Goal,
                item.Status,
                item.StartedAt,
                item.FinishedAt))
            .FirstOrDefaultAsync(ct);

        if (session is null) return Results.NotFound();

        var agents = await LoadAgentsAsync(db, ct);
        var run = (await BuildRunItemsAsync(db, tenantId, agents, new[] { session }, clock.UtcNow, ct)).Single();

        var traces = await db.AgentTraces
            .AsNoTracking()
            .Where(trace => trace.SessionId == sessionId)
            .OrderBy(trace => trace.OccurredAt)
            .Select(trace => new TaskRunTraceResponse(
                trace.Id,
                trace.SessionId,
                trace.TaskId,
                trace.AgentName ?? "Agent",
                trace.Phase ?? "trace",
                trace.Message ?? string.Empty,
                trace.OccurredAt))
            .ToListAsync(ct);

        var auditQuery = db.AuditLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(audit => audit.TenantId == tenantId && audit.ResourceId == sessionId);

        if (session.AgentId.HasValue)
        {
            auditQuery = auditQuery.Union(db.AuditLogs
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(audit => audit.TenantId == tenantId && audit.ResourceId == session.AgentId.Value));
        }

        var auditEvents = await auditQuery
            .OrderByDescending(audit => audit.OccurredAt)
            .Take(25)
            .Select(audit => new TaskRunAuditResponse(
                audit.Id,
                audit.Action,
                audit.ResourceType,
                audit.ResourceId,
                audit.DiffJson,
                audit.IpAddress == null ? null : audit.IpAddress.ToString(),
                audit.UserAgent,
                audit.OccurredAt))
            .ToListAsync(ct);

        return Results.Ok(new TaskRunDetailResponse(run, traces, auditEvents));
    }

    private static async Task<IResult> ListAuditAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        [FromQuery] string? action,
        [FromQuery] string? resourceType,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var tenantId = tenants.Require().TenantId;
        if (pageSize is < 1 or > 200) pageSize = 50;

        var query = db.AuditLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(audit => audit.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(audit => audit.Action == action.Trim());
        if (!string.IsNullOrWhiteSpace(resourceType))
            query = query.Where(audit => audit.ResourceType == resourceType.Trim());

        var key = KeysetQuery.Decode(cursor);
        int? total = key is null ? await query.CountAsync(ct) : null;
        if (key is not null)
        {
            var ts = key.Value.Ts;
            var id = key.Value.Id;
            query = query.Where(audit =>
                audit.OccurredAt < ts || (audit.OccurredAt == ts && audit.Id < id));
        }

        var fetched = await query
            .OrderByDescending(audit => audit.OccurredAt)
            .ThenByDescending(audit => audit.Id)
            .Take(pageSize + 1)
            .Select(audit => new TaskRunAuditResponse(
                audit.Id,
                audit.Action,
                audit.ResourceType,
                audit.ResourceId,
                audit.DiffJson,
                audit.IpAddress == null ? null : audit.IpAddress.ToString(),
                audit.UserAgent,
                audit.OccurredAt))
            .ToListAsync(ct);

        var (rows, nextCursor) = KeysetQuery.SliceWithCursor(fetched, pageSize, r => r.OccurredAt, r => r.Id);
        return Results.Ok(new AuditLogCursorPage(rows, nextCursor, total));
    }

    private static async Task<IReadOnlyList<TaskRunListItemResponse>> BuildRunItemsAsync(
        AppDbContext db,
        Guid tenantId,
        IReadOnlyList<AgentRow> agents,
        IReadOnlyList<SessionRow> sessions,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (sessions.Count == 0) return [];

        var sessionIds = sessions.Select(session => session.Id).ToArray();
        var traces = await db.AgentTraces
            .AsNoTracking()
            .Where(trace => sessionIds.Contains(trace.SessionId))
            .Select(trace => new TraceRow(
                trace.SessionId,
                trace.AgentName,
                trace.Phase,
                trace.Message,
                trace.OccurredAt))
            .ToListAsync(ct);

        var agentById = agents.ToDictionary(agent => agent.Id);
        var minStarted = sessions.Min(session => session.StartedAt);
        var maxFinished = sessions.Max(session => session.FinishedAt ?? now);
        var agentCodes = sessions
            .Where(session => session.AgentId.HasValue && agentById.ContainsKey(session.AgentId.Value))
            .Select(session => agentById[session.AgentId!.Value].Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        IReadOnlyList<CostRow> costs = agentCodes.Length == 0
            ? []
            : await db.LlmCostLedger
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(cost =>
                    cost.TenantId == tenantId &&
                    cost.AgentCode != Clawbot.Domain.Agents.LlmCostEntry.ReservationAgentCode &&
                    cost.CreatedAt >= minStarted &&
                    cost.CreatedAt <= maxFinished &&
                    agentCodes.Contains(cost.AgentCode))
                .Select(cost => new CostRow(cost.AgentCode, cost.InputTokens, cost.OutputTokens, cost.Usd, cost.CreatedAt))
                .ToListAsync(ct);

        return sessions.Select(session =>
        {
            var agent = session.AgentId.HasValue && agentById.TryGetValue(session.AgentId.Value, out var foundAgent)
                ? foundAgent
                : null;
            var traceRows = traces
                .Where(trace => trace.SessionId == session.Id)
                .OrderBy(trace => trace.OccurredAt)
                .ToArray();
            var lastTrace = traceRows.LastOrDefault();
            var end = session.FinishedAt ?? now;
            IReadOnlyList<CostRow> sessionCosts = agent is null
                ? []
                : costs.Where(cost =>
                        string.Equals(cost.AgentCode, agent.Code, StringComparison.OrdinalIgnoreCase) &&
                        cost.CreatedAt >= session.StartedAt &&
                        cost.CreatedAt <= end)
                    .ToArray();

            return new TaskRunListItemResponse(
                session.Id,
                agent?.Code,
                agent?.DisplayName ?? lastTrace?.AgentName ?? "Agent",
                agent?.AgentType ?? "unknown",
                string.IsNullOrWhiteSpace(session.Goal) ? "Không có mô tả tác vụ" : session.Goal!,
                session.Status,
                session.StartedAt,
                session.FinishedAt,
                Math.Max(0, Math.Round((end - session.StartedAt).TotalMilliseconds)),
                traceRows.Length,
                lastTrace?.Phase,
                lastTrace?.Message,
                sessionCosts.Sum(cost => cost.InputTokens + cost.OutputTokens),
                sessionCosts.Sum(cost => cost.Usd));
        }).ToList();
    }

    private static async Task<TaskRunStatsResponse> BuildStatsAsync(AppDbContext db, Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var sessionIds = db.AgentSessions.AsNoTracking().Select(session => session.Id);
        var since = now.AddDays(-30);
        var tokens = await db.LlmCostLedger
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(cost =>
                cost.TenantId == tenantId &&
                cost.AgentCode != Clawbot.Domain.Agents.LlmCostEntry.ReservationAgentCode &&
                cost.CreatedAt >= since &&
                cost.CreatedAt <= now)
            .SumAsync(cost => (int?)(cost.InputTokens + cost.OutputTokens), ct) ?? 0;

        return new TaskRunStatsResponse(
            await db.AgentSessions.AsNoTracking().CountAsync(ct),
            await db.AgentSessions.AsNoTracking().CountAsync(session => session.Status == "running", ct),
            await db.AgentSessions.AsNoTracking().CountAsync(session => session.Status == "completed", ct),
            await db.AgentTraces.AsNoTracking().CountAsync(trace => sessionIds.Contains(trace.SessionId), ct),
            await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().CountAsync(audit => audit.TenantId == tenantId, ct),
            tokens);
    }

    private static async Task<IReadOnlyList<AgentRow>> LoadAgentsAsync(AppDbContext db, CancellationToken ct)
        => await db.AgentConfigs
            .AsNoTracking()
            .Where(agent => agent.DeletedAt == null)
            .Select(agent => new AgentRow(agent.Id, agent.Code, agent.DisplayName, agent.AgentType))
            .ToListAsync(ct);

    private static string EscapeLikePattern(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal);

    private sealed record AgentRow(Guid Id, string Code, string DisplayName, string AgentType);
    private sealed record SessionRow(Guid Id, Guid? AgentId, string? Goal, string Status, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt);
    private sealed record TraceRow(Guid SessionId, string? AgentName, string? Phase, string? Message, DateTimeOffset OccurredAt);
    private sealed record CostRow(string AgentCode, int InputTokens, int OutputTokens, decimal Usd, DateTimeOffset CreatedAt);
}
