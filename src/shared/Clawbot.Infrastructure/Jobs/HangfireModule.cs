using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Infrastructure.Jobs;

public static class HangfireModule
{
    private static readonly string[] QueueNames = { "default", "retention", "kpi", "content", "ads" };

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
        services.AddScoped<AnomalyAlertJob>();
        services.AddScoped<ForecastPrecomputeJob>();
        services.AddScoped<IWeeklyTrendScanner, GrpcWeeklyTrendScanner>();
        services.AddScoped<WeeklyTrendScanJob>();
        services.AddScoped<ContentPublishJob>();
        services.AddScoped<AdsRuleEvaluationJob>();
        services.AddScoped<AdsCreativeRotationJob>();
        services.AddScoped<AdsRemarketingJob>();
        services.AddScoped<AdsLookalikeRefreshJob>();
        services.AddScoped<AdsDaypartPauseJob>();
        services.AddScoped<AdsDaypartResumeJob>();
        services.AddScoped<WeeklyAdsReportJob>();
        services.AddScoped<AutoSummaryJob>();
        services.AddScoped<HealthCheckJob>();
        services.AddScoped<OutOfHoursAutoReplyJob>();
        services.AddScoped<DripSequenceJob>();
        services.AddScoped<IdleConversationAlertJob>();
        services.AddScoped<LeadFollowUpJob>();
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
            Cron.Daily(0, 30));
        recurring.AddOrUpdate<AnomalyAlertJob>(
            "kpi-anomaly-alert",
            "kpi",
            j => j.RunAsync(CancellationToken.None),
            "0 * * * *");
        recurring.AddOrUpdate<ForecastPrecomputeJob>(
            "kpi-forecast-precompute",
            "kpi",
            j => j.RunAsync(CancellationToken.None),
            "45 0 * * *");
        recurring.AddOrUpdate<WeeklyTrendScanJob>(
            "content-weekly-trend-scan",
            "content",
            j => j.RunAsync(CancellationToken.None),
            Cron.Weekly(DayOfWeek.Monday, 0, 0));
        recurring.AddOrUpdate<ContentPublishJob>(
            "content-publish-due",
            "content",
            j => j.RunAsync(CancellationToken.None),
            "*/5 * * * *");
        recurring.AddOrUpdate<AdsRuleEvaluationJob>(
            "ads-rule-evaluation",
            "ads",
            j => j.RunAsync(CancellationToken.None),
            "0 */4 * * *");
        recurring.AddOrUpdate<AdsCreativeRotationJob>(
            "ads-creative-rotation",
            "ads",
            j => j.RunAsync(CancellationToken.None),
            Cron.Daily(3));
        recurring.AddOrUpdate<AdsRemarketingJob>(
            "ads-remarketing",
            "ads",
            j => j.RunAsync(CancellationToken.None),
            Cron.Daily(4));
        recurring.AddOrUpdate<AdsLookalikeRefreshJob>(
            "ads-lookalike-refresh",
            "ads",
            j => j.RunAsync(CancellationToken.None),
            Cron.Weekly(DayOfWeek.Monday, 1, 0));
        recurring.AddOrUpdate<AdsDaypartPauseJob>(
            "ads-daypart-pause",
            "ads",
            j => j.RunAsync(CancellationToken.None),
            "0 19 * * *");
        recurring.AddOrUpdate<AdsDaypartResumeJob>(
            "ads-daypart-resume",
            "ads",
            j => j.RunAsync(CancellationToken.None),
            "0 22 * * *");
        recurring.AddOrUpdate<WeeklyAdsReportJob>(
            "ads-weekly-report",
            "ads",
            j => j.RunAsync(CancellationToken.None),
            Cron.Weekly(DayOfWeek.Monday, 2, 0));
        recurring.AddOrUpdate<HealthCheckJob>(
            "health-check",
            "default",
            j => j.RunAsync(CancellationToken.None),
            Cron.Hourly);
        recurring.AddOrUpdate<OutOfHoursAutoReplyJob>(
            "out-of-hours-auto-reply",
            "default",
            j => j.RunAsync(CancellationToken.None),
            "*/10 * * * *");
        recurring.AddOrUpdate<DripSequenceJob>(
            "drip-sequence-sender",
            "default",
            j => j.RunAsync(CancellationToken.None),
            "*/5 * * * *");
        recurring.AddOrUpdate<IdleConversationAlertJob>(
            "idle-conversation-alert",
            "default",
            j => j.RunAsync(CancellationToken.None),
            "*/2 * * * *");
        recurring.AddOrUpdate<LeadFollowUpJob>(
            "lead-followup",
            "default",
            j => j.RunAsync(CancellationToken.None),
            "0 * * * *");
    }
}
