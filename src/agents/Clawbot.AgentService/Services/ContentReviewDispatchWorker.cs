using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Clawbot.AgentService.Services;

public interface IReviewTenantRunner
{
    Task RunTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}

// Phase 2.3: system-level loop that enumerates active tenants and runs ReviewTenantWorker
// in a fresh scope per tenant. Does not hold work across tenants.
public sealed partial class ContentReviewDispatchWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ContentReviewWorkerOptions> options,
    ILogger<ContentReviewDispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval);
        await DispatchOnceAsync(stoppingToken).ConfigureAwait(false);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            await DispatchOnceAsync(stoppingToken).ConfigureAwait(false);
    }

    public async Task DispatchOnceAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> tenantIds;
        using (var enumerationScope = scopeFactory.CreateScope())
        {
            var db = enumerationScope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await db.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(tenant => tenant.IsActive)
                .OrderBy(tenant => tenant.Id)
                .Select(tenant => tenant.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var tenantId in tenantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var tenantScope = scopeFactory.CreateScope();
                var runner = tenantScope.ServiceProvider
                    .GetRequiredService<IReviewTenantRunner>();
                await runner.RunTenantAsync(tenantId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogTenantDispatchFailed(logger, exception, tenantId);
            }
        }
    }

    [LoggerMessage(
        EventId = 1130,
        Level = LogLevel.Error,
        Message = "Failed to dispatch content review for tenant {TenantId}")]
    private static partial void LogTenantDispatchFailed(
        ILogger logger,
        Exception exception,
        Guid tenantId);
}
