using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddScoped<DailySummaryJob>();
        services.AddScoped<DailyKpiRollupJob>();
        services.AddScoped<RefreshTokenCleanupJob>();
        services.AddScoped<DailyReportJob>();
        services.AddScoped<AnomalyAlertJob>();
        services.AddScoped<ForecastPrecomputeJob>();
        services.AddScoped<IWeeklyTrendScanner, GrpcWeeklyTrendScanner>();
        services.AddScoped<WeeklyTrendScanJob>();
        services.AddScoped<ContentPublishJob>();
        services.AddScoped<MetaConnectionHealthJob>();
        services.AddScoped<MetaBusinessIntegrationWebhookJob>();
        services.AddScoped<AdsRuleEvaluationJob>();
        services.AddScoped<AdsCreativeRotationJob>();
        services.AddScoped<AdsRemarketingJob>();
        services.AddScoped<AdsLookalikeRefreshJob>();
        services.AddScoped<AdsDaypartPauseJob>();
        services.AddScoped<AdsDaypartResumeJob>();
        services.AddScoped<WeeklyAdsReportJob>();
        services.AddScoped<AutoSummaryJob>();
        services.AddScoped<CommentAutoReplyJob>();
        services.AddScoped<HealthCheckJob>();
                services.AddScoped<DripSequenceJob>();
        services.AddScoped<IIdleEscalationRecipientResolver, SalesLeadIdleEscalationRecipientResolver>();
        services.AddScoped<IdleConversationAlertJob>();
        // Review-gate P4: nhắc/escalate bài chờ review sát giờ đăng.
        services.AddScoped<IContentReviewEscalationRecipientResolver, ContentReviewEscalationRecipientResolver>();
        services.AddScoped<ContentReviewSlaJob>();
        services.AddScoped<LeadFollowUpJob>();
        services.AddScoped<KbAccuracyTestJob>();
        services.AddScoped<CompetitorScanJob>();
        // ai-self-learning-memory Lớp 1: chưng cất tri thức đêm. ContentReviewer đăng ký tại đây vì
        // API host không gọi AddClawbotContent (chỉ AgentService có) — job cần reviewer chấm đề xuất.
        services.Configure<Clawbot.Infrastructure.Learning.LearningOptions>(cfg.GetSection(Clawbot.Infrastructure.Learning.LearningOptions.SectionName));
        services.TryAddScoped<Clawbot.Agents.Core.Content.ContentReviewer>();
        services.AddScoped<Clawbot.Agents.Core.Learning.KnowledgeDistiller>();
        services.AddScoped<Clawbot.Agents.Core.Learning.KbSuggestionAccuracyEvaluator>();
        services.AddScoped<Clawbot.Infrastructure.Learning.KbSuggestionMaterializer>();
        services.AddScoped<KnowledgeDistillationJob>();
        // Lớp 2: trích facts về khách sau hội thoại idle.
        services.AddScoped<Clawbot.Agents.Core.Learning.ContactFactExtractor>();
        services.AddScoped<ContactMemoryExtractionJob>();
        return services;
    }

    private static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        foreach (var id in new[] { "SE Asia Standard Time", "Asia/Ho_Chi_Minh", "Asia/Bangkok" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.CreateCustomTimeZone("VN+7", TimeSpan.FromHours(7), "Vietnam (UTC+7)", "Vietnam (UTC+7)");
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
        recurring.AddOrUpdate<RefreshTokenCleanupJob>(
            "refresh-token-cleanup",
            "default",
            j => j.RunAsync(CancellationToken.None),
            Cron.Daily(3));
        recurring.AddOrUpdate<DailyReportJob>(
            "daily-report-push",
            "kpi",
            j => j.RunAsync(CancellationToken.None),
            Cron.Daily(7, 30),
            new RecurringJobOptions { TimeZone = VietnamTimeZone });
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
            Cron.Weekly(DayOfWeek.Monday, 7, 0),
            new RecurringJobOptions { TimeZone = VietnamTimeZone });
        recurring.AddOrUpdate<ContentPublishJob>(
            "content-publish-due",
            "content",
            j => j.RunAsync(CancellationToken.None),
            "*/5 * * * *");
        recurring.AddOrUpdate<ContentReviewSlaJob>(
            "content-review-sla",
            "content",
            j => j.RunAsync(CancellationToken.None),
            "*/5 * * * *");
        recurring.AddOrUpdate<MetaConnectionHealthJob>(
            "meta-connection-health",
            "default",
            j => j.RunAsync(CancellationToken.None),
            Cron.Daily(2, 15));
        // Comment auto-reply đường polling: consumer bus chạy đa host không enqueue Hangfire được,
        // scan quét comment mới (idempotent) thay thế.
        recurring.AddOrUpdate<CommentAutoReplyJob>(
            "comment-auto-reply-scan",
            "default",
            j => j.RunScanAsync(CancellationToken.None),
            "*/2 * * * *");
        recurring.AddOrUpdate<AdsRuleEvaluationJob>(
            "ads-rule-evaluation",
            "ads",
            j => j.RunAsync(CancellationToken.None),
            "0 * * * *");
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
        recurring.AddOrUpdate<KbAccuracyTestJob>(
            "kb-accuracy-check",
            "kpi",
            j => j.RunAsync(CancellationToken.None),
            Cron.Daily(1));
        recurring.AddOrUpdate<DailySummaryJob>(
            "inbox-daily-summary",
            "default",
            j => j.RunAsync(CancellationToken.None),
            Cron.Daily(21),
            new RecurringJobOptions { TimeZone = VietnamTimeZone });
        recurring.AddOrUpdate<CompetitorScanJob>(
            "competitor-scan",
            "content",
            j => j.RunAsync(CancellationToken.None),
            Cron.Daily(6),
            new RecurringJobOptions { TimeZone = VietnamTimeZone });
        // AI tự học: chưng cất tri thức từ hội thoại thật — 02:00 giờ VN hằng đêm.
        recurring.AddOrUpdate<KnowledgeDistillationJob>(
            "kb-knowledge-distillation",
            "default",
            j => j.RunAsync(CancellationToken.None),
            Cron.Daily(2),
            new RecurringJobOptions { TimeZone = VietnamTimeZone });
        // AI tự học Lớp 2: trích memory về khách từ hội thoại idle — 30 phút/lần.
        recurring.AddOrUpdate<ContactMemoryExtractionJob>(
            "contact-memory-extraction",
            "default",
            j => j.RunScanAsync(CancellationToken.None),
            "*/30 * * * *");
    }
}
