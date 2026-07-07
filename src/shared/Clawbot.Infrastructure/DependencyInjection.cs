using Clawbot.Agents.Core.Ads;
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
using Clawbot.Infrastructure.Email;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Ads;
using Clawbot.Infrastructure.Leads;
using Clawbot.SharedKernel.Audit;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Multitenancy;
using Clawbot.Infrastructure.Persistence;
using Clawbot.Infrastructure.Resilience;
using Clawbot.Infrastructure.Security;
using Clawbot.Infrastructure.Time;
using Clawbot.Infrastructure.Vectors;
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
        services.AddClawbotPiiRedactor(); // AuditSaveChangesInterceptor depends on IPiiRedactor.
        services.AddScoped<Messaging.DomainEventDispatchInterceptor>();

        services.AddDbContext<AppDbContext>((sp, opt) =>
        {
            opt.UseSqlServer(cfg.GetConnectionString("SqlServer"));
            opt.AddInterceptors(
                sp.GetRequiredService<AuditSaveChangesInterceptor>(),
                sp.GetRequiredService<Messaging.DomainEventDispatchInterceptor>());
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

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIntentClassifier, KeywordIntentClassifier>();
        services.AddScoped<ITenantAccessor, HttpTenantAccessor>();
        services.AddScoped<ITenantResolver, DemoTenantResolver>();
        services.Configure<EncryptionOptions>(cfg.GetSection("Encryption"));
        services.AddSingleton<IEncryptor, AesEncryptor>();
        // Per-(tenant, agent) LLM provider resolution (decrypts the bound LlmConfig at call time).
        services.AddSingleton<Clawbot.Agents.Core.Chat.ILlmConfigResolver, Agents.LlmConfigResolver>();
        services.AddScoped<IEmbeddingConfigResolver, Agents.EmbeddingConfigResolver>();
        services.AddScoped<IActiveKbVersionResolver, Agents.ActiveKbVersionResolver>();
        services.Configure<PublisherOptions>(cfg.GetSection(PublisherOptions.SectionName));
        services.AddSingleton<IGoldenHourResolver, DefaultGoldenHourResolver>();
        services.AddClawbotLead(); // Lead-2: least-busy assignment for API endpoints + hot-lead consumer

        services.AddCompetitorMonitor(); // Research-2: competitor feed scanner (typed HttpClient)
        services.AddScoped<IChannelMessageIngestor, ChannelMessageIngestor>();
        services.AddScoped<IKpiAggregator, KpiAggregator>();
        services.AddScoped<ILeadDedupService, EfLeadDedupService>();
        services.AddScoped<IAssignmentPoolSource, EfAssignmentPoolSource>();
        services.AddClawbotLead(); // ILeadAssignmentService, consumed by LeadsEndpoints.
        services.AddScoped<IPancakeConfigResolver, PancakeConfigResolver>();
        // SPEC-16 §5.1: per-page Pancake token model — page-token read resolver + mint/store service + HTTP gateway.
        var pancakeUserApi = cfg.GetSection(PancakeUserApiOptions.SectionName).Get<PancakeUserApiOptions>() ?? new PancakeUserApiOptions();
        services.AddSingleton(pancakeUserApi);
        services.AddScoped<IPancakePageTokenResolver, PancakePageTokenResolver>();
        services.AddScoped<IPancakePageTokenService, PancakePageTokenService>();
        services.AddHttpClient<IPageTokenMintGateway, HttpPancakePageTokenMintGateway>()
            .AddPolicyHandler(HttpResiliencePolicies.Retry())
            .AddPolicyHandler(HttpResiliencePolicies.CircuitBreaker())
            .AddPolicyHandler(HttpResiliencePolicies.Timeout(TimeSpan.FromSeconds(15)));
        // SPEC-16 Module M-3/M-4: same gateway also lists pages (IPageListGateway) for the admin connect flow.
        services.AddScoped<IPageListGateway>(sp => sp.GetRequiredService<IPageTokenMintGateway>() as IPageListGateway
            ?? throw new InvalidOperationException("IPageListGateway not available"));

        services.AddHttpClient<IChannelAdapter, PancakeChannelAdapter>()
            .AddPolicyHandler(HttpResiliencePolicies.Retry())
            .AddPolicyHandler(HttpResiliencePolicies.CircuitBreaker())
            .AddPolicyHandler(HttpResiliencePolicies.Timeout(TimeSpan.FromSeconds(10)));
        // SPEC-16 P2-8: graph publisher (FB Graph /feed + Zalo OA) when GraphPublisher is enabled; otherwise the
        // legacy generic webhook publisher (HttpSocialPublisher) stays the default for backward compatibility.
        services.Configure<GraphPublisherOptions>(cfg.GetSection(GraphPublisherOptions.SectionName));
        // SPEC-16 Module M-1: encrypted DB credential resolver for FB/Zalo (falls back to options in GraphSocialPublisher).
        services.AddScoped<ISocialCredentialResolver, EfSocialCredentialResolver>();
        var graphPublisherOptions = cfg.GetSection(GraphPublisherOptions.SectionName).Get<GraphPublisherOptions>() ?? new GraphPublisherOptions();
        var graphPublisherEnabled = graphPublisherOptions.Facebook.Enabled || graphPublisherOptions.Zalo.Enabled;
        if (graphPublisherEnabled)
        {
            services.AddHttpClient<ISocialPublisher, GraphSocialPublisher>()
                .AddPolicyHandler(HttpResiliencePolicies.Retry())
                .AddPolicyHandler(HttpResiliencePolicies.CircuitBreaker())
                .AddPolicyHandler(HttpResiliencePolicies.Timeout(TimeSpan.FromSeconds(15)));
        }
        else
        {
            services.AddHttpClient<ISocialPublisher, HttpSocialPublisher>()
                .AddPolicyHandler(HttpResiliencePolicies.Retry())
                .AddPolicyHandler(HttpResiliencePolicies.CircuitBreaker())
                .AddPolicyHandler(HttpResiliencePolicies.Timeout(TimeSpan.FromSeconds(10)));
        }

        // Ads connectors
        services.Configure<MetaAdsOptions>(cfg.GetSection(MetaAdsOptions.SectionName));
        services.Configure<TikTokAdsOptions>(cfg.GetSection(TikTokAdsOptions.SectionName));
        services.AddSingleton<IAdsPlatformThrottle, AdsPlatformThrottle>();
        services.AddHttpClient<IAdsPlatformConnector, MetaAdsConnector>()
            .AddPolicyHandler(HttpResiliencePolicies.Retry())
            .AddPolicyHandler(HttpResiliencePolicies.CircuitBreaker())
            .AddPolicyHandler(HttpResiliencePolicies.Timeout(TimeSpan.FromSeconds(10)));
        services.AddHttpClient<IAdsPlatformConnector, TikTokAdsConnector>()
            .AddPolicyHandler(HttpResiliencePolicies.Retry())
            .AddPolicyHandler(HttpResiliencePolicies.CircuitBreaker())
            .AddPolicyHandler(HttpResiliencePolicies.Timeout(TimeSpan.FromSeconds(10)));
        services.AddSingleton<IAdsConnectorResolver, AdsConnectorResolver>();
        services.AddSingleton<AdsAgent>();

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
