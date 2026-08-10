using Clawbot.Agents.Core.Lead;
using Clawbot.Agents.Core.Rag;
using Clawbot.Agents.Core.Skills;
using Clawbot.Agents.Core.Skills.Content;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Application.Abstractions;
using Clawbot.Infrastructure.Analytics;
using Clawbot.Infrastructure.Audit;
using Clawbot.Infrastructure.Channels;
using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Email;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.Infrastructure.Leads;
using Clawbot.Infrastructure.Multitenancy;
using Clawbot.Infrastructure.Persistence;
using Clawbot.Infrastructure.Resilience;
using Clawbot.Infrastructure.Security;
using Clawbot.Infrastructure.Time;
using Clawbot.Infrastructure.Vectors;
using Clawbot.SharedKernel.Audit;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Time;
using Clawbot.SharedKernel.Vectors;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client;
using StackExchange.Redis;

namespace Clawbot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration cfg)
    {
        services.AddHttpContextAccessor();

        services.AddScoped<IAuditContext, HttpAuditContext>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddSingleton<Clawbot.Infrastructure.Observability.RequestStatsCounter>();
        services.AddClawbotPiiRedactor(); // AuditSaveChangesInterceptor depends on IPiiRedactor.
        services.AddScoped<Messaging.DomainEventDispatchInterceptor>();
        // Phase 6.1: stamp SESSION_CONTEXT writer version on every AppDbContext SQL connection.
        services.Configure<Clawbot.Infrastructure.Content.ContentWorkflowWriterOptions>(
            cfg.GetSection(Clawbot.Infrastructure.Content.ContentWorkflowWriterOptions.SectionName));
        services.AddSingleton<ContentWorkflowWriterSessionInterceptor>();
        services.AddMemoryCache();
        services.AddScoped<Clawbot.Infrastructure.Content.IContentWorkflowRuntimeGate,
            Clawbot.Infrastructure.Content.ContentWorkflowRuntimeGate>();
        // ai-self-learning-memory Lớp 3: memory theo agent — ContentReviewer nạp "lỗi hay gặp" vào persona.
        // Đăng ký ở đây để CẢ 2 host (API + AgentService) đều có; reviewer coi provider là optional.
        services.AddScoped<Clawbot.Agents.Core.Learning.IAgentMemoryProvider, Clawbot.Infrastructure.Learning.EfAgentMemoryProvider>();

        services.AddDbContext<AppDbContext>((sp, opt) =>
        {
            opt.UseSqlServer(cfg.GetConnectionString("SqlServer"));
            opt.AddInterceptors(
                sp.GetRequiredService<AuditSaveChangesInterceptor>(),
                sp.GetRequiredService<Messaging.DomainEventDispatchInterceptor>(),
                sp.GetRequiredService<ContentWorkflowWriterSessionInterceptor>());
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // Identity and Auth
        services.AddIdentityCore<AppUser>(opt =>
            {
                opt.User.RequireUniqueEmail = true;
                opt.Password.RequiredLength = 8;
                // SPEC-11: lockout policy from AuthPolicy (code is source of truth).
                opt.Lockout.MaxFailedAccessAttempts = AuthPolicy.MaxFailedAccessAttempts;
                opt.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(AuthPolicy.LockoutMinutes);
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
        services.AddAuthentication();

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(cfg.GetConnectionString("Redis") ?? "localhost:6379"));

        // SPEC-11 auth services: refresh-token rotation + runtime permission resolution.
        // PostConfigure forces the timing from AuthPolicy so appsettings cannot drift it.
        services.Configure<Auth.RefreshTokenOptions>(cfg.GetSection("RefreshToken"));
        services.PostConfigure<Auth.RefreshTokenOptions>(o =>
        {
            o.Days = AuthPolicy.RefreshTokenDays;
            o.GraceSeconds = AuthPolicy.RefreshGraceSeconds;
        });
        services.AddScoped<Auth.IRefreshTokenService, Auth.RefreshTokenService>();
        services.AddScoped<Auth.IPermissionResolver, Auth.PermissionResolver>();

        services.AddMassTransit(bus =>
        {
            bus.AddConsumer<Messaging.ConversationEscalatedConsumer>();
            bus.AddConsumer<Messaging.LeadBecameHotConsumer>();
            bus.AddConsumer<Messaging.LeadBecameWarmConsumer>();
            bus.AddConsumer<Messaging.LeadBecameCustomerConsumer>();
            bus.AddConsumer<Messaging.LeadReactivatedConsumer>();
            // Chat inbound pipeline: polling publishes, this consumer ingests (ordered, retried)
            bus.AddConsumer<Messaging.ChannelInboundMessageConsumer, Messaging.ChannelInboundMessageConsumerDefinition>();

            // WS1: transactional outbox - domain events published during SaveChanges enlist into
            // OutboxMessage within the same transaction, then relay to RabbitMQ (exactly-once,
            // durable across broker outage). Tables created by migration 0015_masstransit_outbox.sql.
            bus.AddEntityFrameworkOutbox<AppDbContext>(o =>
            {
                o.QueryDelay = TimeSpan.FromSeconds(10);
                o.UseSqlServer();
                o.UseBusOutbox();
            });

            bus.UsingRabbitMq((ctx, mq) =>
            {
                mq.Host(cfg.GetConnectionString("RabbitMq") ?? "amqp://guest:guest@localhost:5672");
                mq.ConfigureEndpoints(ctx);
            });
        });

        // AI auto-reply: consumer goi ChatAgent gRPC (AgentService) khi hoi thoai bat co "AI dang chat".
        // Dang ky o shared DI vi consumer chay o ca API lan AgentService host (AgentService tu goi chinh no).
        var chatAgentUrl = cfg["AgentService:Url"] ?? "http://localhost:15875";
        services.AddGrpcClient<Clawbot.Agents.Contracts.Chat.ChatAgent.ChatAgentClient>(o =>
        {
            o.Address = new Uri(chatAgentUrl);
        });
        services.AddScoped<Messaging.IChatAutoReplyGateway, Messaging.GrpcChatAutoReplyGateway>();
        // Trả lời tin khách treo khi AI (vừa) bật lại — dùng chung cho toggle tay + sweep AiAutoReplyResumeJob
        // + debouncer gom tin (consumer chỉ đặt đồng hồ, hết cửa sổ resumer trả lời cả khối một lần).
        services.AddScoped<Messaging.IAiAutoReplyResumer, Messaging.AiAutoReplyResumer>();
        services.Configure<Messaging.AiAutoReplyOptions>(cfg.GetSection(Messaging.AiAutoReplyOptions.SectionName));
        services.AddSingleton<Messaging.IAiReplyDebouncer, Messaging.AiReplyDebouncer>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIntentClassifier, KeywordIntentClassifier>();
        services.AddScoped<ITenantAccessor, HttpTenantAccessor>();
        services.AddScoped<ITenantResolver, DemoTenantResolver>();

        services.Configure<MetaGraphOptions>(cfg.GetSection(MetaGraphOptions.SectionName));
        services.AddScoped<MetaGraphConfigurationStore>();
        services.AddScoped<IMetaGraphConfigurationResolver>(sp => sp.GetRequiredService<MetaGraphConfigurationStore>());
        services.AddScoped<IMetaAppConfigurationService>(sp => sp.GetRequiredService<MetaGraphConfigurationStore>());
        services.AddHttpClient<IMetaGraphClient, MetaGraphClient>((sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MetaGraphOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 120));
        }).RemoveAllLoggers();
        services.AddScoped<IMetaIntegrationService, MetaIntegrationService>();
        services.AddScoped<Channels.Meta.IMetaInboxProvisioner, Channels.Meta.MetaInboxProvisioner>();
        services.Configure<EncryptionOptions>(cfg.GetSection("Encryption"));
        services.AddSingleton<IEncryptor, AesEncryptor>();
        // Per-(tenant, agent) LLM provider resolution (decrypts the bound LlmConfig at call time).
        services.AddSingleton<Clawbot.Agents.Core.Chat.ILlmConfigResolver, Agents.LlmConfigResolver>();
        services.AddScoped<IEmbeddingConfigResolver, Agents.EmbeddingConfigResolver>();
        services.AddScoped<IActiveKbVersionResolver, Agents.ActiveKbVersionResolver>();
        // LLM-mode retrieval: đọc ContentMd KB deployed từ SQL, không cần Qdrant/embedding.
        services.AddScoped<IKbContentReader, Agents.KbContentReader>();
        services.Configure<PublisherOptions>(cfg.GetSection(PublisherOptions.SectionName));
        services.AddSingleton<IGoldenHourResolver, DefaultGoldenHourResolver>();
        // Phase 3.2: revision-bound golden-hour schedule intent in the approval transaction.
        services.AddScoped<Clawbot.Infrastructure.Content.IContentAutoScheduler,
            Clawbot.Infrastructure.Content.ContentAutoScheduler>();
        services.AddClawbotLead(); // Lead-2: least-busy assignment for API endpoints + hot-lead consumer

        services.AddCompetitorMonitor(); // Research-2: competitor feed scanner (typed HttpClient)
        services.AddScoped<IChannelMessageIngestor, ChannelMessageIngestor>();
        services.AddScoped<IKpiAggregator, KpiAggregator>();
        services.AddScoped<ILeadDedupService, EfLeadDedupService>();
        services.AddScoped<IAssignmentPoolSource, EfAssignmentPoolSource>();
        services.AddClawbotLead(); // ILeadAssignmentService, consumed by LeadsEndpoints.
        services.AddSingleton<Clawbot.Agents.Core.Skills.Lead.KeywordLeadSignalClassifier>();
        services.AddScoped<Clawbot.Infrastructure.Leads.LeadBatchRescorer>();
        services.AddScoped<Clawbot.Infrastructure.Leads.ILeadNotificationRecipientResolver,
            Clawbot.Infrastructure.Leads.LeadNotificationRecipientResolver>();
        services.AddScoped<IPancakeConfigResolver, PancakeConfigResolver>();
        // SPEC-16 §5.1: per-page Pancake token model — page-token read resolver + mint/store service + HTTP gateway.
        var pancakeUserApi = cfg.GetSection(PancakeUserApiOptions.SectionName).Get<PancakeUserApiOptions>() ?? new PancakeUserApiOptions();
        services.AddSingleton(pancakeUserApi);
        services.AddScoped<IPancakePageTokenResolver, PancakePageTokenResolver>();
        services.AddScoped<IPancakePageTokenService, PancakePageTokenService>();
        services.AddHttpClient<IPageTokenMintGateway, HttpPancakePageTokenMintGateway>()
            .ConfigurePrimaryHttpMessageHandler(PancakeEndpointPolicy.CreateNoRedirectHandler)
            .RemoveAllLoggers()
            .AddPolicyHandler(HttpResiliencePolicies.CircuitBreaker())
            .AddPolicyHandler(HttpResiliencePolicies.Timeout(TimeSpan.FromSeconds(15)));
        // SPEC-16 Module M-3/M-4: same gateway also lists pages (IPageListGateway) for the admin connect flow.
        services.AddScoped<IPageListGateway>(sp => sp.GetRequiredService<IPageTokenMintGateway>() as IPageListGateway
            ?? throw new InvalidOperationException("IPageListGateway not available"));

        // Outbound message POSTs are not idempotent; never auto-retry after an ambiguous response.
        services.AddHttpClient<IChannelAdapter, PancakeChannelAdapter>()
            .ConfigurePrimaryHttpMessageHandler(PancakeEndpointPolicy.CreateNoRedirectHandler)
            .RemoveAllLoggers()
            .AddPolicyHandler(HttpResiliencePolicies.CircuitBreaker())
            .AddPolicyHandler(HttpResiliencePolicies.Timeout(TimeSpan.FromSeconds(10)));
        // Comment auto-reply: cùng instance adapter Pancake, expose thêm action reply_comment/private_replies.
        services.AddScoped<ICommentChannelAdapter>(sp =>
            sp.GetRequiredService<IChannelAdapter>() as ICommentChannelAdapter
            ?? throw new InvalidOperationException("ICommentChannelAdapter not available"));
        services.AddScoped<Channels.Meta.MetaCommentChannelAdapter>();
        services.AddScoped<Channels.Meta.ICommentChannelAdapterResolver, Channels.Meta.TenantCommentChannelAdapterResolver>();
        // Native Facebook/Zalo publishing is always available so DB-backed connections take effect without a restart.
        services.Configure<GraphPublisherOptions>(cfg.GetSection(GraphPublisherOptions.SectionName));
        services.AddScoped<EfSocialCredentialResolver>();
        services.AddScoped<ISocialCredentialResolver>(sp => sp.GetRequiredService<EfSocialCredentialResolver>());
        services.AddScoped<IInstagramCredentialResolver>(sp => sp.GetRequiredService<EfSocialCredentialResolver>());
        services.AddSingleton<IHostAddressResolver, SystemHostAddressResolver>();
        services.AddSingleton<IPublicUrlSafetyValidator, DnsPublicUrlSafetyValidator>();
        services.AddTransient<ZaloResponseBufferingHandler>();
        services.AddHttpClient(
                GraphSocialPublisher.ZaloHttpClientName,
                static client =>
                {
                    client.Timeout = Timeout.InfiniteTimeSpan;
                    client.MaxResponseContentBufferSize = GraphSocialPublisher.ZaloMaxResponseContentBufferSize;
                })
            .RedactLoggedHeaders(static headerName =>
                string.Equals(headerName, "access_token", StringComparison.OrdinalIgnoreCase))
            .ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler
            {
                AllowAutoRedirect = false,
                MaxResponseHeadersLength = checked(
                    (int)(GraphSocialPublisher.ZaloMaxResponseContentBufferSize / 1024)),
                UseCookies = false,
            })
            .AddPolicyHandler(HttpResiliencePolicies.CircuitBreaker())
            .AddPolicyHandler(HttpResiliencePolicies.Timeout(TimeSpan.FromSeconds(15)))
            .AddHttpMessageHandler<ZaloResponseBufferingHandler>();
        services.AddHttpClient<GraphSocialPublisher>()
            .AddPolicyHandler(HttpResiliencePolicies.CircuitBreaker())
            .AddPolicyHandler(HttpResiliencePolicies.Timeout(TimeSpan.FromSeconds(15)));
        services.AddHttpClient<HttpSocialPublisher>()
            .AddPolicyHandler(HttpResiliencePolicies.CircuitBreaker())
            .AddPolicyHandler(HttpResiliencePolicies.Timeout(TimeSpan.FromSeconds(10)));
        services.AddScoped<ISocialPublisher, RoutingSocialPublisher>();

        // Vector store: Qdrant is the only supported backend now SQL Server doesn't carry pgvector.
        services.Configure<QdrantOptions>(cfg.GetSection(QdrantOptions.SectionName));
        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<QdrantOptions>>().Value;
            return new QdrantClient(o.Host, o.Port, o.UseTls, o.ApiKey);
        });
        services.AddSingleton<IVectorStore, QdrantVectorStore>();
        // Scoped: depends on IEmbeddingProvider which captures the scoped (DbContext-backed)
        // IEmbeddingConfigResolver. A singleton here is a captive-dependency error.
        services.AddScoped<IContactEmbeddingSync, ContactEmbeddingSync>();

        // External-service config modules (Options pattern, no raw cfg[] reads).
        services.Configure<SmtpOptions>(cfg.GetSection(SmtpOptions.SectionName));
        services.Configure<Documents.MinioOptions>(cfg.GetSection(Documents.MinioOptions.SectionName));
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<Clawbot.Agents.Core.Kb.IDocumentTextExtractor, Documents.DocumentTextExtractor>();

        return services;
    }
}
