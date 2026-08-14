using Clawbot.Infrastructure.Persistence;

namespace Clawbot.AgentService.Services;

public interface IAgentScheduleLeaseProvider
{
    Task<IAsyncDisposable?> TryAcquireAsync(
        AppDbContext db,
        Guid scheduleId,
        CancellationToken ct);
}

internal sealed class AgentScheduleLeaseProvider : IAgentScheduleLeaseProvider
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(
        AppDbContext db,
        Guid scheduleId,
        CancellationToken ct) =>
        await AgentScheduleLease.TryAcquireAsync(db, scheduleId, ct).ConfigureAwait(false);
}
