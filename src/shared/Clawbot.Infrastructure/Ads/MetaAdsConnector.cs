using System.Globalization;
using System.Text.Json;
using Clawbot.Agents.Core.Ads;
using Clawbot.Infrastructure.Integrations.Meta;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Ads;

public sealed partial class MetaAdsConnector(
    IOptions<MetaAdsOptions> options,
    ILogger<MetaAdsConnector> logger,
    IAdsPlatformThrottle throttle,
    IMetaIntegrationService integrations,
    IMetaGraphClient graph) : IAdsPlatformConnector
{
    public string Platform => "meta";

    private readonly MetaAdsOptions _options = options.Value;

    public async Task<AdsMetricSnapshot?> FetchMetricsAsync(
        Guid tenantId,
        string externalCampaignId,
        CancellationToken ct = default)
    {
        var credential = await ResolveCredentialAsync(tenantId, ct).ConfigureAwait(false);
        if (credential is null)
            return null;

        return await throttle.RunAsync(Platform, async throttleCt =>
        {
            try
            {
                using var doc = await graph.GetAsync(
                    tenantId,
                    $"{Uri.EscapeDataString(externalCampaignId)}/insights",
                    new Dictionary<string, string?>
                    {
                        ["fields"] = "cpc,impressions,clicks,spend,actions",
                    },
                    credential.Value.Token,
                    throttleCt).ConfigureAwait(false);
                return ParseMetrics(doc.RootElement);
            }
            catch (MetaGraphException ex)
            {
                if (credential.Value.FromConnection && ex.IsTokenError)
                    await integrations.MarkReconnectRequiredAsync(tenantId, TokenError(ex), throttleCt).ConfigureAwait(false);
                LogFetchFailed(logger, externalCampaignId, ex);
                return null;
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
            {
                LogFetchFailed(logger, externalCampaignId, ex);
                return null;
            }
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool> ApplyActionAsync(
        Guid tenantId,
        string externalCampaignId,
        string action,
        decimal? newBudget,
        CancellationToken ct = default)
    {
        var credential = await ResolveCredentialAsync(tenantId, ct).ConfigureAwait(false);
        if (credential is null)
            return false;

        return await throttle.RunAsync(Platform, async throttleCt =>
        {
            try
            {
                var status = action switch
                {
                    "pause" => "PAUSED",
                    "scale_up" or "scale_down" => "ACTIVE",
                    _ => null,
                };
                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                if (status is not null)
                    fields["status"] = status;
                if (newBudget.HasValue)
                    fields["daily_budget"] = decimal.Truncate(newBudget.Value * 100m).ToString(CultureInfo.InvariantCulture);
                if (fields.Count == 0)
                    return false;

                using var doc = await graph.PostAsync(
                    tenantId,
                    Uri.EscapeDataString(externalCampaignId),
                    fields,
                    credential.Value.Token,
                    throttleCt).ConfigureAwait(false);
                return !doc.RootElement.TryGetProperty("success", out var success)
                    || success.ValueKind == JsonValueKind.True;
            }
            catch (MetaGraphException ex)
            {
                if (credential.Value.FromConnection && ex.IsTokenError)
                    await integrations.MarkReconnectRequiredAsync(tenantId, TokenError(ex), throttleCt).ConfigureAwait(false);
                LogActionFailed(logger, action, externalCampaignId, ex);
                return false;
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
            {
                LogActionFailed(logger, action, externalCampaignId, ex);
                return false;
            }
        }, ct).ConfigureAwait(false);
    }

    public Task<string?> BuildLookalikeAsync(
        Guid tenantId,
        IReadOnlyList<string> seedContactKeys,
        CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task<bool> BuildRemarketingAsync(
        Guid tenantId,
        string audienceName,
        IReadOnlyList<string> contactKeys,
        CancellationToken ct = default) =>
        Task.FromResult(false);

    private async Task<(string Token, bool FromConnection)?> ResolveCredentialAsync(Guid tenantId, CancellationToken ct)
    {
        if (!_options.Enabled)
            return null;

        var connectionToken = await integrations.ResolveRootTokenAsync(tenantId, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(connectionToken))
            return (connectionToken, true);
        if (!string.IsNullOrWhiteSpace(_options.AccessToken))
            return (_options.AccessToken, false);
        return null;
    }

    private static string TokenError(MetaGraphException ex) => $"meta_token_{ex.Code}_{ex.Subcode}";

    [LoggerMessage(EventId = 5401, Level = LogLevel.Warning, Message = "Meta ads metrics fetch failed for campaign {CampaignId}")]
    private static partial void LogFetchFailed(ILogger logger, string campaignId, Exception exception);

    [LoggerMessage(EventId = 5402, Level = LogLevel.Warning, Message = "Meta ads action {Action} failed for campaign {CampaignId}")]
    private static partial void LogActionFailed(ILogger logger, string action, string campaignId, Exception exception);

    internal static AdsMetricSnapshot? ParseMetrics(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ParseMetrics(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static AdsMetricSnapshot? ParseMetrics(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array
            || data.GetArrayLength() == 0)
            return null;

        var row = data[0];
        var spend = DecimalString(row, "spend");
        var impressions = IntString(row, "impressions");
        var clicks = IntString(row, "clicks");
        var cpl = clicks > 0 ? spend / clicks : 0m;
        var ctr = impressions > 0 ? (decimal)clicks / impressions * 100 : 0m;
        return new AdsMetricSnapshot(cpl, 1m, ctr, spend, 0m);
    }

    private static decimal DecimalString(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value)
        && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;

    private static int IntString(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value)
        && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
}
