using System.Collections.Concurrent;
using System.Globalization;
using Clawbot.Agents.Core.Chat;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Agents;

// Loads an agent's bound LlmConfig, validates it is active, decrypts the key, and computes the
// effective model. Singleton: opens a short-lived scope per resolve for DbContext access and uses
// explicit tenant filters (the gRPC AgentService path has no ITenantAccessor).
// Chưa bind = throw (bind là chủ ý); binding trỏ config ĐÃ TẮT/XÓA = fallback sang config active
// cũ nhất của tenant (cùng thứ tự seeder chọn default) để agent không chết khi admin tắt config cũ.
public sealed partial class LlmConfigResolver(
    IServiceScopeFactory scopeFactory,
    IEncryptor encryptor,
    ILogger<LlmConfigResolver>? logger = null) : ILlmConfigResolver
{
    // Cảnh báo thiếu đơn giá lặp mỗi lượt resolve sẽ ngập log -> log 1 lần cho mỗi (config, lần sửa);
    // admin sửa config là UpdatedAt đổi nên cảnh báo tự bật lại nếu vẫn để trống đơn giá.
    private static readonly ConcurrentDictionary<string, byte> WarnedMissingRates = new(StringComparer.Ordinal);

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IEncryptor _encryptor = encryptor;
    private readonly ILogger<LlmConfigResolver>? _logger = logger;

    public async Task<ResolvedLlmConfig> ResolveAsync(Guid tenantId, string agentCode, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var normalizedAgentCode = Clawbot.Agents.Core.AgentPromptPacks.NormalizeCode(agentCode);
        var agent = await db.AgentConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.Code == normalizedAgentCode && a.DeletedAt == null)
            .Select(a => new { a.LlmConfigId, a.Model })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var definitionConfigId = agent is null
            ? await db.AgentDefinitions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(a => a.TenantId == tenantId && a.Code == agentCode && a.DeletedAt == null)
                .Select(a => a.LlmConfigId)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : null;

        var configId = agent?.LlmConfigId ?? definitionConfigId;
        if (configId is null)
            throw new LlmConfigNotConfiguredException(tenantId, agentCode);

        var cfg = await db.LlmConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == configId.Value && c.TenantId == tenantId && c.IsActive)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var fellBack = false;
        if (cfg is null)
        {
            // Binding cũ trỏ config đã tắt/xóa — fallback config active cũ nhất thay vì chặn agent.
            cfg = await db.LlmConfigs
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.IsActive)
                .OrderBy(c => c.CreatedAt)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (cfg is null)
                throw new LlmConfigNotConfiguredException(tenantId, agentCode);
            fellBack = true;
            if (_logger is not null)
                LogFallback(_logger, agentCode, tenantId, configId.Value, cfg.Id);
        }

        // D2: compiled agents may override the config model. Data-defined agents use LlmConfig.ModelId.
        // Khi fallback: bỏ model override của binding gốc — model đó đi kèm provider cũ, có thể không
        // tồn tại trên config fallback. ConfigModelId giữ model chuẩn của config để caller retry
        // khi provider chốt lỗi cấp model (model override cũ chết, model config còn sống).
        var effectiveModel = fellBack || string.IsNullOrWhiteSpace(agent?.Model) ? cfg.ModelId : agent!.Model;
        string apiKey;
        try
        {
            apiKey = _encryptor.Decrypt(cfg.ApiKeyEncrypted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new LlmConfigNotConfiguredException(tenantId, agentCode);
        }

        WarnMissingRates(cfg.Id, cfg.UpdatedAt, cfg.Provider, effectiveModel, cfg.InputUsdPer1M, cfg.OutputUsdPer1M);

        return new ResolvedLlmConfig(
            cfg.Provider,
            effectiveModel,
            apiKey,
            cfg.BaseUrl,
            cfg.InputUsdPer1M,
            cfg.OutputUsdPer1M,
            cfg.TimeoutSeconds,
            cfg.MaxOutputTokens,
            ConfigId: cfg.Id,
            ConfigUpdatedAt: cfg.UpdatedAt,
            SupportsVision: cfg.SupportsVision,
            ConfigModelId: cfg.ModelId);
    }

    // Đơn giá NULL -> client LLM âm thầm dùng mặc định 3.00/15.00 USD/1M (giá Claude). Áp giá đó cho
    // model provider khác cho ra chi phí sai lệch nhiều lần mà không ai biết -> phải cảnh báo rõ.
    private void WarnMissingRates(
        Guid configId, DateTimeOffset updatedAt, string provider, string model, decimal? inputRate, decimal? outputRate)
    {
        if (_logger is null || (inputRate is not null && outputRate is not null))
            return;

        var key = string.Create(
            CultureInfo.InvariantCulture, $"{configId}|{updatedAt.UtcTicks}");
        if (!WarnedMissingRates.TryAdd(key, 0))
            return;

        LogMissingRates(_logger, configId, provider, model, inputRate, outputRate);
    }

    [LoggerMessage(EventId = 7302, Level = LogLevel.Warning,
        Message = "LLM config {ConfigId} ({Provider}/{Model}) thiếu đơn giá (input={InputRate}, output={OutputRate}): hệ thống đang áp mặc định 3.00/15.00 USD/1M của Claude, chi phí báo cáo sẽ sai nếu provider có giá khác. Hãy nhập đơn giá thật trong màn hình cấu hình LLM.")]
    private static partial void LogMissingRates(
        ILogger logger, Guid configId, string provider, string model, decimal? inputRate, decimal? outputRate);

    [LoggerMessage(EventId = 7301, Level = LogLevel.Warning,
        Message = "LLM config fallback for agent {AgentCode} tenant {TenantId}: bound config {BoundConfigId} is inactive/missing, using active config {FallbackConfigId}")]
    private static partial void LogFallback(ILogger logger, string agentCode, Guid tenantId, Guid boundConfigId, Guid fallbackConfigId);
}
