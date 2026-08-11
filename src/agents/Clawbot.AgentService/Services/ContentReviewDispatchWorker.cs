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
        await RunTickAsync(stoppingToken).ConfigureAwait(false);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            await RunTickAsync(stoppingToken).ConfigureAwait(false);
    }

    // Câu truy vấn liệt kê tenant nằm NGOÀI try/catch của từng tenant: SQL Server chớp tắt là nó ném
    // thẳng ra khỏi ExecuteAsync, và mặc định BackgroundServiceExceptionBehavior = StopHost giết cả
    // host — dừng luôn cả hàng chờ agent review. Bọc từng nhịp để chỉ mất một nhịp.
    private async Task RunTickAsync(CancellationToken stoppingToken)
    {
        try
        {
            await DispatchOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogDispatchTickFailed(logger, exception);
        }
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

    [LoggerMessage(
        EventId = 1131,
        Level = LogLevel.Error,
        Message = "Content review dispatch tick failed; worker keeps running and retries next tick")]
    private static partial void LogDispatchTickFailed(ILogger logger, Exception exception);
}
