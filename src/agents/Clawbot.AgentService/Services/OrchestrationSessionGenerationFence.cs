using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.AgentService.Services;

// Serializes orchestration-generated side effects with plan replacement and terminalization.
// Callers must hold a database transaction while using the locked session.
internal enum OrchestrationItemReviewEligibility
{
    Eligible,
    Deferred,
    Reject,
}

internal static class OrchestrationSessionGenerationFence
{
    public static async Task EnsureCurrentAsync(
        AppDbContext db,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        if (context.SessionId is null && context.OrchestrationPlanGeneration is null)
            return;
        if (context.SessionId is not { } sessionId
            || context.OrchestrationPlanGeneration is not { } planGeneration
            || planGeneration < 0)
        {
            throw new OrchestrationPlanGenerationMismatchException();
        }

        var session = await LockAsync(db, context.TenantId, sessionId, cancellationToken)
            .ConfigureAwait(false);
        if (session is null
            || session.Status != AgentSessionStatuses.Running
            || session.ReplanCount != planGeneration)
        {
            throw new OrchestrationPlanGenerationMismatchException();
        }
    }

    public static async Task<OrchestrationItemReviewEligibility> ResolveReviewEligibilityAsync(
        AppDbContext db,
        ContentItem item,
        CancellationToken cancellationToken)
    {
        if (item.OrchestrationSessionId is null || item.OrchestrationOwnershipClaimedAt is not null)
            return OrchestrationItemReviewEligibility.Eligible;
        if (item.OrchestrationPlanGeneration is not { } planGeneration)
            return OrchestrationItemReviewEligibility.Reject;

        var session = await LockAsync(
                db,
                item.TenantId,
                item.OrchestrationSessionId.Value,
                cancellationToken)
            .ConfigureAwait(false);
        if (session is null || session.ReplanCount != planGeneration)
            return OrchestrationItemReviewEligibility.Reject;

        return session.Status switch
        {
            AgentSessionStatuses.Running or AgentSessionStatuses.Completed => OrchestrationItemReviewEligibility.Eligible,
            AgentSessionStatuses.Failed => OrchestrationItemReviewEligibility.Reject,
            _ => OrchestrationItemReviewEligibility.Deferred,
        };
    }

    public static async Task<AgentSession?> LockAsync(
        AppDbContext db,
        Guid tenantId,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || sessionId == Guid.Empty)
            return null;

        AgentSession? session;
        if (db.Database.IsSqlServer())
        {
            session = await db.AgentSessions
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM dbo.agent_sessions WITH (UPDLOCK, HOLDLOCK)
                    WHERE id = {sessionId} AND tenant_id = {tenantId}
                    """)
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            session = await db.AgentSessions
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == sessionId && candidate.TenantId == tenantId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // A long-running orchestration scope can already track this session. Tracking queries retain those cached
        // properties, so refresh after acquiring the database lock before comparing plan generation or status.
        if (session is not null)
            await db.Entry(session).ReloadAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }
}
