using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Clawbot.Infrastructure.Jobs;

public static class HangfireModule
{
    // "ai": job LLM chạy vài phút — để chung "default" là nghẽn cả retention/kpi khi có 1 lô sinh tài liệu.
    private static readonly string[] QueueNames = { "default", "retention", "kpi", "content", "ads", "ai" };

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
        services.AddScoped<RequestStatsFlushJob>();
        services.AddScoped<DailySummaryJob>();
        services.AddScoped<DailyKpiRollupJob>();
        services.AddScoped<RefreshTokenCleanupJob>();
        services.AddScoped<DailyReportJob>();
        services.AddScoped<AnomalyAlertJob>();
        services.AddScoped<ForecastPrecomputeJob>();
        services.AddScoped<IWeeklyTrendScanner, GrpcWeeklyTrendScanner>();
        services.AddScoped<WeeklyTrendScanJob>();
        services.AddScoped<ContentPublishJob>();
        // Phase 3.9: outcome_unknown never blind-retried; privileged reconcile / provider lookup.
        services.AddScoped<ContentPublishAttemptReconciliationJob>();
        // Phase 6.7: durable content workflow health (review debt / held / outcome_unknown / pause).
        services.Configure<ContentWorkflowHealthOptions>(
            cfg.GetSection(ContentWorkflowHealthOptions.SectionName));
        services.AddScoped<ContentWorkflowHealthJob>();
        services.AddScoped<MetaEngagementSyncJob>();
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
        // Sale gửi tay -> AI nhường tạm; job này bật lại + trả lời tin khách treo khi hết cửa sổ.
        services.AddScoped<AiAutoReplyResumeJob>();
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
        // Ước tính doanh thu lead từ hội thoại (launch từ API khi → customer, không có revenue).
        services.AddScoped<Clawbot.Agents.Core.Lead.LeadRevenueEstimator>();
        services.AddScoped<Clawbot.Infrastructure.Leads.ILeadNotificationRecipientResolver,
            Clawbot.Infrastructure.Leads.LeadNotificationRecipientResolver>();
        services.AddScoped<Clawbot.Infrastructure.Leads.LeadRevenueEstimateService>();
        // Lớp 3: bài học cho reviewer từ lý do reject + nén KB weekly.
        services.AddScoped<Clawbot.Agents.Core.Learning.AgentMistakeExtractor>();
        services.AddScoped<AgentMemoryDistillationJob>();
        services.AddScoped<KbCompressionJob>();
        // Nền "chạy ngầm — thông báo — click xem trạng thái": launcher + runner dùng chung cho mọi job do user kích.
        // Handler (IJobHandler) đăng ký ở host API cùng chỗ với endpoint sinh ra nó.
        services.AddScoped<Clawbot.SharedKernel.Jobs.IJobLauncher, HangfireJobLauncher>();
        services.AddScoped<JobRunner>();
        // Web Push: thiếu VAPID key thì WebPushDispatchJob tự thoát — feed + chuông vẫn chạy.
        services.Configure<Clawbot.Infrastructure.Notifications.WebPushOptions>(
            cfg.GetSection(Clawbot.Infrastructure.Notifications.WebPushOptions.SectionName));
        // Typed client thôi: KHÔNG bọc thêm AddScoped(sp => sp.GetRequiredService<PushServiceClient>())
        // — đăng ký sau đè lên chính nó, resolve là đệ quy vô hạn. VAPID truyền theo từng lần gửi.
        services.AddHttpClient<Lib.Net.Http.WebPush.PushServiceClient>();
        services.AddScoped<WebPushDispatchJob>();
        services.AddScoped<UnreadFailureEmailJob>();
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
        recurring.AddOrUpdate<RequestStatsFlushJob>(
            "request-stats-flush",
            "default",
            j => j.RunAsync(CancellationToken.None),
            "* * * * *");
        recurring.AddOrUpdate<DailyKpiRollupJob>(
            "kpi-daily-rollup",
            "kpi",
            j => j.RunAsync(CancellationToken.None),
            Cron.Daily(0, 30));
        recurring.AddOrUpdate<UnreadFailureEmailJob>(
            "unread-failure-email",
            "default",
            j => j.RunAsync(CancellationToken.None),
            "*/15 * * * *");
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
        recurring.AddOrUpdate<ContentPublishAttemptReconciliationJob>(
            "content-publish-reconcile",
            "content",
            j => j.RunAsync(CancellationToken.None),
            "*/10 * * * *");
        recurring.AddOrUpdate<ContentReviewSlaJob>(
            "content-review-sla",
            "content",
            j => j.RunAsync(CancellationToken.None),
            "*/5 * * * *");
        recurring.AddOrUpdate<ContentWorkflowHealthJob>(
            "content-workflow-health",
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
        // Refresh like/comment counts on published Facebook posts (Graph API — Pancake has no metric).
        recurring.AddOrUpdate<MetaEngagementSyncJob>(
            "meta-engagement-sync",
            "default",
            j => j.RunAsync(CancellationToken.None),
            "*/15 * * * *");
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
        // Sale gửi tay -> AI nhường tạm; hết cửa sổ (tenant config, mặc định 5') thì bật lại + trả lời tin treo.
        recurring.AddOrUpdate<AiAutoReplyResumeJob>(
            "ai-auto-reply-resume",
            "default",
            j => j.RunAsync(CancellationToken.None),
            "* * * * *");
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
        // AI tự học Lớp 3: bài học reviewer từ lý do reject — 01:30 giờ VN (trước distillation 02:00).
        recurring.AddOrUpdate<AgentMemoryDistillationJob>(
            "agent-memory-distillation",
            "default",
            j => j.RunAsync(CancellationToken.None),
            "30 1 * * *",
            new RecurringJobOptions { TimeZone = VietnamTimeZone });
        // AI tự học 3.2: nén/gộp KB trùng lắp — Chủ nhật 03:00 giờ VN, đề xuất merge luôn chờ người.
        recurring.AddOrUpdate<KbCompressionJob>(
            "kb-compression",
            "default",
            j => j.RunAsync(CancellationToken.None),
            Cron.Weekly(DayOfWeek.Sunday, 3, 0),
            new RecurringJobOptions { TimeZone = VietnamTimeZone });
    }
}
