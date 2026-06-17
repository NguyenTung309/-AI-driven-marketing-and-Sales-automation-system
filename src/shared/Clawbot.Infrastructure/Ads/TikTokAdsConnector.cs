using System.Text.Json;
using Clawbot.Agents.Core.Ads;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Ads;

public sealed partial class TikTokAdsConnector(
    HttpClient http,
    IOptions<TikTokAdsOptions> options,
    ILogger<TikTokAdsConnector> logger,
    IAdsPlatformThrottle throttle) : IAdsPlatformConnector
{
    public string Platform => "tiktok";

    private readonly TikTokAdsOptions _options = options.Value;

    public async Task<AdsMetricSnapshot?> FetchMetricsAsync(string externalCampaignId, CancellationToken ct = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.AccessToken))
            return null;

        return await throttle.RunAsync(Platform, async throttleCt =>
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(throttleCt);
                timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

                var url = $"{_options.Endpoint}/report/integrated/get?advertiser_id={Uri.EscapeDataString(_options.AdvertiserId)}&campaign_ids=[\"{externalCampaignId}\"]&data_level=CAMPAIGN&dimensions=[\"campaign_id\"]&metrics=[\"cpc\",\"impression\",\"click\",\"spend\",\"frequency\",\"ctr\"]";
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Absolute));
                request.Headers.Add("Access-Token", _options.AccessToken);
                using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                return ParseMetrics(body);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
            {
                LogFetchFailed(logger, externalCampaignId, ex);
                return null;
            }
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool> ApplyActionAsync(string externalCampaignId, string action, decimal? newBudget, CancellationToken ct = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.AccessToken))
            return false;

        return await throttle.RunAsync(Platform, async throttleCt =>
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(throttleCt);
                timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

                var status = action switch
                {
                    "pause" => "CAMPAIGN_STATUS_DISABLE",
                    "scale_up" or "scale_down" => "CAMPAIGN_STATUS_ENABLE",
                    _ => null,
                };

                var url = $"{_options.Endpoint}/campaign/update?advertiser_id={Uri.EscapeDataString(_options.AdvertiserId)}";
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(url, UriKind.Absolute));
                request.Headers.Add("Access-Token", _options.AccessToken);

                var body = new Dictionary<string, object> { ["campaign_id"] = externalCampaignId };
                if (status is not null)
                    body["operation_status"] = status;
                if (newBudget.HasValue)
                    body["budget"] = newBudget.Value;

                request.Content = new StringContent(
                    JsonSerializer.Serialize(body),
                    System.Text.Encoding.UTF8,
                    "application/json");

                using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
            {
                LogActionFailed(logger, action, externalCampaignId, ex);
                return false;
            }
        }, ct).ConfigureAwait(false);
    }

    public Task<string?> BuildLookalikeAsync(IReadOnlyList<string> seedContactKeys, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task<bool> BuildRemarketingAsync(string audienceName, IReadOnlyList<string> contactKeys, CancellationToken ct = default) =>
        Task.FromResult(false);

    [LoggerMessage(EventId = 5403, Level = LogLevel.Warning, Message = "TikTok ads metrics fetch failed for campaign {CampaignId}")]
    private static partial void LogFetchFailed(ILogger logger, string campaignId, Exception exception);

    [LoggerMessage(EventId = 5404, Level = LogLevel.Warning, Message = "TikTok ads action {Action} failed for campaign {CampaignId}")]
    private static partial void LogActionFailed(ILogger logger, string action, string campaignId, Exception exception);

    internal static AdsMetricSnapshot? ParseMetrics(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data))
                return null;
            if (!data.TryGetProperty("list", out var list) || list.GetArrayLength() == 0)
                return null;

            var row = list[0];
            var metrics = row.TryGetProperty("metrics", out var m) ? m : default;
            if (metrics.ValueKind == JsonValueKind.Undefined)
                return null;

            var spend = metrics.TryGetProperty("spend", out var s) ? s.GetDecimal() : 0m;
            var impressions = metrics.TryGetProperty("impression", out var i) ? i.GetInt32() : 0;
            var clicks = metrics.TryGetProperty("click", out var c) ? c.GetInt32() : 0;
            var frequency = metrics.TryGetProperty("frequency", out var f) ? f.GetDecimal() : 1m;
            var ctr = metrics.TryGetProperty("ctr", out var cr) ? cr.GetDecimal() : 0m;
            var cpc = metrics.TryGetProperty("cpc", out var cp) ? cp.GetDecimal() : 0m;

            return new AdsMetricSnapshot(cpc, frequency, ctr, spend, 0m);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
