using Clawbot.Agents.Core.Ads;
using Clawbot.Agents.Core.Lead;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Application.Abstractions;
using Clawbot.Infrastructure.Analytics;
using Clawbot.Infrastructure.Audit;
using Clawbot.Infrastructure.Channels;
using Clawbot.Infrastructure.Channels.Pancake;
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

        services.AddDbContext<AppDbContext>((sp, opt) =>
        {
            opt.UseSqlServer(cfg.GetConnectionString("SqlServer"));
            opt.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddIdentityCore<AppUser>(opt =>
            {
                opt.User.RequireUniqueEmail = true;
                opt.Password.RequiredLength = 8;
                opt.Lockout.MaxFailedAccessAttempts = 5;
                opt.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
        services.AddAuthentication();

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(cfg.GetConnectionString("Redis") ?? "localhost:6379"));

        services.AddMassTransit(bus =>
        {
            bus.UsingRabbitMq((ctx, mq) =>
            {
                mq.Host(cfg.GetConnectionString("RabbitMq") ?? "amqp://guest:guest@localhost:5672");
                mq.ConfigureEndpoints(ctx);
            });
        });

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ITenantAccessor, HttpTenantAccessor>();
        services.Configure<EncryptionOptions>(cfg.GetSection("Encryption"));
        services.AddSingleton<IEncryptor, AesEncryptor>();
        services.Configure<PublisherOptions>(cfg.GetSection(PublisherOptions.SectionName));
        services.AddSingleton<IGoldenHourResolver, DefaultGoldenHourResolver>();

        services.AddScoped<IChannelMessageIngestor, ChannelMessageIngestor>();
        services.AddScoped<IKpiAggregator, KpiAggregator>();
        services.AddScoped<ILeadDedupService, EfLeadDedupService>();
        services.AddScoped<IAssignmentPoolSource, EfAssignmentPoolSource>();
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
        services.AddSingleton(_ => new QdrantClient(cfg["Vector:Qdrant:Host"] ?? "localhost"));
        services.AddScoped<IVectorStore, QdrantVectorStore>();

        return services;
    }
}
