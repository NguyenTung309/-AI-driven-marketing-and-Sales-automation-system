using Clawbot.Domain.Llm;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Identity;

/// <summary>
/// Development-only seeder for a local OpenAI-compatible LLM provider so orchestration
/// can run without external vendor credentials. The API key is read from the environment
/// (never source), encrypted via <see cref="IEncryptor"/>, and bound to orchestrator-facing
/// agent definitions. Never wire into production.
/// </summary>
public static partial class DemoLlmConfigSeeder
{
    public const string EnvKeyName = "CLAWBOT_DEMO_LLM_API_KEY";
    public const string DisplayName = "Local OpenAI-compatible demo";
    private const string Provider = "openai-compatible";
    private const string ModelId = "cx/gpt-5.5";
    private const string BaseUrl = "http://localhost:20128/v1";

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var apiKey = Environment.GetEnvironmentVariable(EnvKeyName);
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DemoLlmConfigSeeder");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            LogNoKey(logger, EnvKeyName);
            return;
        }

        var db = sp.GetRequiredService<AppDbContext>();
        var encryptor = sp.GetRequiredService<IEncryptor>();

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug == DevDataSeeder.TenantSlug, ct).ConfigureAwait(false);
        if (tenant is null)
        {
            LogNoTenant(logger);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var encrypted = encryptor.Encrypt(apiKey);

        var config = await db.LlmConfigs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenant.Id && c.DisplayName == DisplayName, ct)
            .ConfigureAwait(false);

        if (config is null)
        {
            config = LlmConfig.Create(tenant.Id, Provider, ModelId, encrypted, now, BaseUrl, DisplayName);
            db.LlmConfigs.Add(config);
        }
        else
        {
            config.UpdateConnection(Provider, ModelId, BaseUrl, DisplayName, now);
            config.RotateApiKey(encrypted, now);
            config.Activate(now);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await BindAgentsAsync(db, tenant.Id, config.Id, now, ct).ConfigureAwait(false);

        LogSeeded(logger, DevDataSeeder.TenantSlug);
    }

    // Bind orchestrator + any orchestratable sub-agent definitions that have no provider yet.
    private static async Task BindAgentsAsync(AppDbContext db, Guid tenantId, Guid llmConfigId, DateTimeOffset now, CancellationToken ct)
    {
        var agents = await db.AgentConfigs.IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.LlmConfigId == null && a.DeletedAt == null)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var agent in agents)
            agent.BindLlmConfig(llmConfigId, now);

        var definitions = await db.AgentDefinitions.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && d.LlmConfigId == null && d.DeletedAt == null)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var def in definitions)
            def.UpdateDefinition(def.DisplayName, def.AgentType, def.PersonaPrompt, def.AllowedToolsJson, def.InputSchemaJson,
                def.OutputSchemaJson, def.MemoryScope, llmConfigId, def.IsOrchestratable, now, def.KbModuleCode);

        if (agents.Count > 0 || definitions.Count > 0)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 1110, Level = LogLevel.Information, Message = "DemoLlmConfigSeeder: seeded local OpenAI-compatible LLM config for tenant {Slug}")]
    private static partial void LogSeeded(ILogger logger, string slug);

    [LoggerMessage(EventId = 1111, Level = LogLevel.Information, Message = "DemoLlmConfigSeeder: {EnvKeyName} not set, skipped local LLM seed")]
    private static partial void LogNoKey(ILogger logger, string envKeyName);

    [LoggerMessage(EventId = 1112, Level = LogLevel.Warning, Message = "DemoLlmConfigSeeder: default tenant not found, skipped local LLM seed")]
    private static partial void LogNoTenant(ILogger logger);
}
