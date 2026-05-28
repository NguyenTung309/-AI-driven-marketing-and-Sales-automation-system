using Clawbot.Application.Abstractions;
using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Multitenancy;
using Clawbot.Infrastructure.Persistence;
using Clawbot.Infrastructure.Resilience;
using Clawbot.Infrastructure.Security;
using Clawbot.Infrastructure.Time;
using Clawbot.Infrastructure.Vectors;
using Clawbot.SharedKernel.Channels;
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

        services.AddDbContext<AppDbContext>(opt =>
            opt.UseSqlServer(cfg.GetConnectionString("SqlServer")));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddIdentityCore<AppUser>()
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

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

        services.AddHttpClient<IChannelAdapter, PancakeChannelAdapter>()
            .AddPolicyHandler(HttpResiliencePolicies.Retry())
            .AddPolicyHandler(HttpResiliencePolicies.CircuitBreaker())
            .AddPolicyHandler(HttpResiliencePolicies.Timeout(TimeSpan.FromSeconds(10)));

        // Vector store: Qdrant is the only supported backend now SQL Server doesn't carry pgvector.
        services.AddSingleton(_ => new QdrantClient(cfg["Vector:Qdrant:Host"] ?? "localhost"));
        services.AddScoped<IVectorStore, QdrantVectorStore>();

        return services;
    }
}
