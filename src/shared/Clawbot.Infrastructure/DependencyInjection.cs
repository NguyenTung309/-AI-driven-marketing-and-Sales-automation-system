using Clawbot.Agents.Core.Ads;
using Clawbot.Agents.Core.Lead;
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

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ITenantAccessor, HttpTenantAccessor>();
        services.AddScoped<ITenantResolver, DemoTenantResolver>();
        services.Configure<EncryptionOptions>(cfg.GetSection("Encryption"));
        services.AddSingleton<IEncryptor, AesEncryptor>();
        services.Configure<PublisherOptions>(cfg.GetSection(PublisherOptions.SectionName));
        services.AddSingleton<IGoldenHourResolver, DefaultGoldenHourResolver>();

        services.AddCompetitorMonitor(); // Research-2: competitor feed scanner (typed HttpClient)
        services.AddScoped<IChannelMessageIngestor, ChannelMessageIngestor>();
        services.AddScoped<IKpiAggregator, KpiAggregator>();
        services.AddScoped<ILeadDedupService, EfLeadDedupService>();
        services.AddScoped<IAssignmentPoolSource, EfAssignmentPoolSource>();
        services.AddClawbotLead(); // ILeadAssignmentService, consumed by LeadsEndpoints.
        services.AddScoped<IPancakeConfigResolver, PancakeConfigResolver>();

        services.AddHttpClient<IChannelAdapter, PancakeChannelAdapter>()
            .AddPolicyHandler(HttpResiliencePolicies.Retry())
            .AddPolicyHandler(HttpResiliencePolicies.CircuitBreaker())
            .AddPolicyHandler(HttpResiliencePolicies.Timeout(TimeSpan.FromSeconds(10)));
        services.AddHttpClient<ISocialPublisher, HttpSocialPublisher>()
            .AddPolicyHandler(HttpResiliencePolicies.Retry())
            .AddPolicyHandler(HttpResiliencePolicies.CircuitBreaker())
            .AddPolicyHandler(HttpResiliencePolicies.Timeout(TimeSpan.FromSeconds(10)));

        // Ads connectors
        services.Configure<MetaAdsOptions>(cfg.GetSection(MetaAdsOptions.SectionName));
        services.Configure<TikTokAdsOptions>(cfg.GetSection(TikTokAdsOptions.SectionName));
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
        services.AddSingleton<IContactEmbeddingSync, ContactEmbeddingSync>();

        // External-service config modules (Options pattern, no raw cfg[] reads).
        services.Configure<SmtpOptions>(cfg.GetSection(SmtpOptions.SectionName));
        services.Configure<Documents.MinioOptions>(cfg.GetSection(Documents.MinioOptions.SectionName));
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        return services;
    }
}
