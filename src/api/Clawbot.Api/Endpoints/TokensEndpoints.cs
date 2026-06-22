using System.Globalization;
using System.Text.Json;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public sealed record TokenUsageResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    int TotalTokens,
    int InputTokens,
    int OutputTokens,
    decimal Usd,
    int MonthlyQuotaTokens,
    int RemainingTokens,
    double UsagePercent,
    int? EstimatedDaysRemaining,
    double? CacheHitRatioPercent,
    IReadOnlyList<TokenAgentUsageResponse> Agents,
    IReadOnlyList<TokenModelUsageResponse> Models,
    TokenAlertSettingsResponse Alert);

public sealed record TokenAgentUsageResponse(
    string Code,
    string DisplayName,
    string AgentType,
    string ModuleName,
    string Status,
    string Model,
    string RouterTier,
    int Calls,
    int InputTokens,
    int OutputTokens,
    int TotalTokens,
    decimal Usd,
    int MonthlyQuotaTokens,
    int AlertPercent,
    double UsagePercent);

public sealed record TokenModelUsageResponse(
    string Model,
    int Calls,
    int TotalTokens,
    decimal Usd,
    double Percent);

public sealed record TokenAlertSettingsResponse(bool Enabled, int LowBalanceThresholdTokens);

public sealed record TokenSettingsRequest(
    IReadOnlyList<TokenQuotaUpdateRequest> Quotas,
    TokenAlertSettingsRequest Alert);

public sealed record TokenQuotaUpdateRequest(
    string Code,
    int MonthlyQuotaTokens,
    int AlertPercent,
    string RouterTier);

public sealed record TokenAlertSettingsRequest(bool Enabled, int LowBalanceThresholdTokens);

public static class TokensEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapTokens(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tokens")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        group.MapGet("/usage", UsageAsync);
        group.MapPut("/settings", UpdateSettingsAsync);

        return group;
    }

    private static async Task<IResult> UsageAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct = default)
    {
        var tenant = tenants.Require();
        var range = ParseRange(from, to);
        if (range.Error is not null) return range.Error;

        var agents = await db.AgentConfigs
            .Where(agent => agent.DeletedAt == null)
            .OrderBy(agent => agent.AgentType)
            .ThenBy(agent => agent.Code)
            .ToListAsync(ct);

        var costs = await db.ClaudeCostLedger
            .IgnoreQueryFilters()
            .Where(cost =>
                cost.TenantId == tenant.TenantId &&
                cost.AgentCode != Clawbot.Domain.Agents.ClaudeCostEntry.ReservationAgentCode &&
                cost.CreatedAt >= range.From &&
                cost.CreatedAt <= range.To)
            .ToListAsync(ct);

        var costByAgent = costs
            .GroupBy(cost => cost.AgentCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var totalInput = costs.Sum(cost => cost.InputTokens);
        var totalOutput = costs.Sum(cost => cost.OutputTokens);
        var totalTokens = totalInput + totalOutput;
        var totalUsd = costs.Sum(cost => cost.Usd);

        var alert = agents.Count > 0 ? ReadTokenConfig(agents[0].ConfigJson).TokenAlerts : new TokenAlertConfig();
        var agentRows = agents.Select(agent =>
        {
            var config = ReadTokenConfig(agent.ConfigJson);
            var agentCosts = costByAgent.GetValueOrDefault(agent.Code) ?? [];
            var input = agentCosts.Sum(cost => cost.InputTokens);
            var output = agentCosts.Sum(cost => cost.OutputTokens);
            var tokens = input + output;
            var quota = Math.Max(1, config.TokenQuota.MonthlyQuotaTokens > 0 ? config.TokenQuota.MonthlyQuotaTokens : DefaultQuota(agent.AgentType));

            return new TokenAgentUsageResponse(
                agent.Code,
                agent.DisplayName,
                agent.AgentType,
                ModuleName(agent.AgentType),
                agent.Status,
                agent.Model,
                NormalizeRouterTier(config.RouterTier, agent.AgentType),
                agentCosts.Count,
                input,
                output,
                tokens,
                agentCosts.Sum(cost => cost.Usd),
                quota,
                Math.Clamp(config.TokenQuota.AlertPercent > 0 ? config.TokenQuota.AlertPercent : DefaultAlertPercent(agent.AgentType), 50, 100),
                Math.Round(tokens * 100d / quota, 1));
        }).ToList();

        var monthlyQuota = agentRows.Sum(row => row.MonthlyQuotaTokens);
        var remaining = Math.Max(0, monthlyQuota - totalTokens);
        var days = (range.To - range.From).TotalDays <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling((range.To - range.From).TotalDays));
        var averageDailyTokens = totalTokens / (double)days;

        var modelRows = costs
            .GroupBy(cost => string.IsNullOrWhiteSpace(cost.Model) ? "unknown" : cost.Model)
            .Select(group =>
            {
                var tokens = group.Sum(cost => cost.InputTokens + cost.OutputTokens);
                return new TokenModelUsageResponse(
                    group.Key,
                    group.Count(),
                    tokens,
                    group.Sum(cost => cost.Usd),
                    totalTokens == 0 ? 0 : Math.Round(tokens * 100d / totalTokens, 1));
            })
            .OrderByDescending(row => row.TotalTokens)
            .ToList();

        return Results.Ok(new TokenUsageResponse(
            range.From,
            range.To,
            totalTokens,
            totalInput,
            totalOutput,
            totalUsd,
            monthlyQuota,
            remaining,
            monthlyQuota == 0 ? 0 : Math.Round(totalTokens * 100d / monthlyQuota, 1),
            averageDailyTokens <= 0 ? null : Math.Max(0, (int)Math.Floor(remaining / averageDailyTokens)),
            null,
            agentRows,
            modelRows,
            new TokenAlertSettingsResponse(alert.Enabled, alert.LowBalanceThresholdTokens)));
    }

    private static async Task<IResult> UpdateSettingsAsync(
        TokenSettingsRequest request,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct = default)
    {
        _ = tenants.Require();
        var agents = await db.AgentConfigs
            .Where(agent => agent.DeletedAt == null)
            .ToListAsync(ct);

        if (agents.Count == 0) return Results.BadRequest(new { error = "no_agents_configured" });

        var quotaByCode = request.Quotas
            .GroupBy(quota => quota.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        var lowBalanceThreshold = Math.Clamp(request.Alert.LowBalanceThresholdTokens, 0, 1_000_000_000);
        foreach (var agent in agents)
        {
            var config = ReadTokenConfig(agent.ConfigJson);
            if (quotaByCode.TryGetValue(agent.Code, out var update))
            {
                config.TokenQuota.MonthlyQuotaTokens = Math.Clamp(update.MonthlyQuotaTokens, 1_000, 1_000_000_000);
                config.TokenQuota.AlertPercent = Math.Clamp(update.AlertPercent, 50, 100);
                config.RouterTier = NormalizeRouterTier(update.RouterTier, agent.AgentType);
            }
            else if (config.TokenQuota.MonthlyQuotaTokens <= 0)
            {
                config.TokenQuota.MonthlyQuotaTokens = DefaultQuota(agent.AgentType);
                config.TokenQuota.AlertPercent = DefaultAlertPercent(agent.AgentType);
                config.RouterTier = NormalizeRouterTier(config.RouterTier, agent.AgentType);
            }

            config.TokenAlerts.Enabled = request.Alert.Enabled;
            config.TokenAlerts.LowBalanceThresholdTokens = lowBalanceThreshold;

            agent.UpdateSettings(
                agent.DisplayName,
                agent.Model,
                agent.SkillFilesJson,
                agent.KbModulesJson,
                JsonSerializer.Serialize(config, JsonOptions),
                clock.UtcNow);
        }

        await db.SaveChangesAsync(ct);
        return await UsageAsync(db, tenants, null, null, ct);
    }

    private static (DateTimeOffset From, DateTimeOffset To, IResult? Error) ParseRange(string? from, string? to)
    {
        var now = DateTimeOffset.UtcNow;
        var parsedTo = string.IsNullOrWhiteSpace(to)
            ? now
            : DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var toValue) ? toValue : default;
        var parsedFrom = string.IsNullOrWhiteSpace(from)
            ? parsedTo.AddDays(-30)
            : DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fromValue) ? fromValue : default;

        if (parsedFrom == default || parsedTo == default)
            return (default, default, Results.BadRequest(new { error = "from/to must be ISO date values" }));
        if (parsedFrom > parsedTo)
            return (default, default, Results.BadRequest(new { error = "from must be before or equal to to" }));

        return (parsedFrom, parsedTo, null);
    }

    private static TokenRuntimeConfig ReadTokenConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new TokenRuntimeConfig();
        try
        {
            return JsonSerializer.Deserialize<TokenRuntimeConfig>(json, JsonOptions) ?? new TokenRuntimeConfig();
        }
        catch (JsonException)
        {
            return new TokenRuntimeConfig();
        }
    }

    private static int DefaultQuota(string agentType) =>
        agentType switch
        {
            "chat" or "sale_assist" => 15_000_000,
            "content" or "research" => 5_000_000,
            "lead" or "report" => 2_000_000,
            _ => 1_000_000,
        };

    private static int DefaultAlertPercent(string agentType) =>
        agentType switch
        {
            "chat" or "sale_assist" => 85,
            "content" or "research" => 90,
            _ => 95,
        };

    private static string ModuleName(string agentType) =>
        agentType switch
        {
            "chat" or "sale_assist" => "Sale",
            "content" or "research" => "Marketing",
            "lead" or "report" or "docs" or "ads" => "Hệ thống",
            _ => "Agent",
        };

    private static string NormalizeRouterTier(string? tier, string agentType)
    {
        var normalized = string.IsNullOrWhiteSpace(tier) ? DefaultRouterTier(agentType) : tier.Trim().ToLowerInvariant();
        return normalized switch
        {
            "flash" => "flash",
            "pro" => "pro",
            "high_effort" or "high-effort" or "vip" => "high_effort",
            _ => DefaultRouterTier(agentType),
        };
    }

    private static string DefaultRouterTier(string agentType) =>
        agentType switch
        {
            "content" or "research" => "pro",
            "report" => "high_effort",
            _ => "flash",
        };

    private sealed class TokenRuntimeConfig
    {
        public string Provider { get; set; } = "claude";
        public string SystemPrompt { get; set; } = string.Empty;
        public double Temperature { get; set; } = 0.4;
        public int MaxTokens { get; set; } = 2048;
        public TokenQuotaConfig TokenQuota { get; set; } = new();
        public string RouterTier { get; set; } = string.Empty;
        public TokenAlertConfig TokenAlerts { get; set; } = new();
    }

    private sealed class TokenQuotaConfig
    {
        public int MonthlyQuotaTokens { get; set; }
        public int AlertPercent { get; set; }
    }

    private sealed class TokenAlertConfig
    {
        public bool Enabled { get; set; } = true;
        public int LowBalanceThresholdTokens { get; set; } = 500_000;
    }
}
