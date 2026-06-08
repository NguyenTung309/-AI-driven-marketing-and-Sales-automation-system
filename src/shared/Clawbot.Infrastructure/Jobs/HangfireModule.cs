using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Infrastructure.Jobs;

public static class HangfireModule
{
    private static readonly string[] QueueNames = { "default", "retention", "kpi" };

    public static IServiceCollection AddClawbotJobs(this IServiceCollection services, IConfiguration cfg)
    {
        var connStr = cfg.GetConnectionString("SqlServer")
            ?? throw new InvalidOperationException("ConnectionStrings:SqlServer required for Hangfire.");

        services.AddHangfire(c => c
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(connStr, new SqlServerStorageOptions
            {
                PrepareSchemaIfNecessary = true,
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.FromSeconds(15),
                DisableGlobalLocks = true,
            }));

        services.AddHangfireServer(o =>
        {
            o.WorkerCount = Math.Max(2, Environment.ProcessorCount / 2);
            o.Queues = QueueNames;
        });

        services.AddScoped<RetentionPurgeJob>();
        services.AddScoped<DailyKpiRollupJob>();
        services.AddScoped<RefreshTokenCleanupJob>();
        return services;
    }

    public static void ScheduleClawbotJobs(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var recurring = services.GetRequiredService<IRecurringJobManager>();
        recurring.AddOrUpdate<RetentionPurgeJob>(
            "retention-purge",
            "retention",
            j => j.RunAsync(CancellationToken.None),
            Cron.Daily(2));
        recurring.AddOrUpdate<DailyKpiRollupJob>(
            "kpi-daily-rollup",
            "kpi",
            j => j.RunAsync(CancellationToken.None),
            Cron.Daily(7, 30));
        recurring.AddOrUpdate<RefreshTokenCleanupJob>(
            "refresh-token-cleanup",
            "default",
            j => j.RunAsync(CancellationToken.None),
            Cron.Daily(3));
    }
}
