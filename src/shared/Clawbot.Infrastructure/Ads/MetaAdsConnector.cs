using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clawbot.Agents.Core.Ads;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Ads;

public sealed partial class MetaAdsConnector(
    HttpClient http,
    IOptions<MetaAdsOptions> options,
    ILogger<MetaAdsConnector> logger) : IAdsPlatformConnector
{
    public string Platform => "meta";

    private readonly MetaAdsOptions _options = options.Value;

    public async Task<AdsMetricSnapshot?> FetchMetricsAsync(string externalCampaignId, CancellationToken ct = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.AccessToken))
            return null;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var url = $"{_options.Endpoint}/{externalCampaignId}/insights?fields=cpc,impressions,clicks,spend,actions&access_token={Uri.EscapeDataString(_options.AccessToken)}";
            var response = await http.GetStringAsync(new Uri(url, UriKind.Absolute), timeout.Token).ConfigureAwait(false);
            return ParseMetrics(response);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            LogFetchFailed(logger, externalCampaignId, ex);
            return null;
        }
    }

    public async Task<bool> ApplyActionAsync(string externalCampaignId, string action, decimal? newBudget, CancellationToken ct = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.AccessToken))
            return false;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            var status = action switch
            {
                "pause" => "PAUSED",
                "scale_up" or "scale_down" => "ACTIVE",
                _ => null,
            };

            var body = new JsonObject();
            if (status is not null)
                body["status"] = status;
            if (newBudget.HasValue)
                body["daily_budget"] = (int)(newBudget.Value * 100);

            var url = $"{_options.Endpoint}/{externalCampaignId}?access_token={Uri.EscapeDataString(_options.AccessToken)}";
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(new Uri(url, UriKind.Absolute), content, timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            LogActionFailed(logger, action, externalCampaignId, ex);
            return false;
        }
    }

    public Task<string?> BuildLookalikeAsync(IReadOnlyList<string> seedContactKeys, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task<bool> BuildRemarketingAsync(string audienceName, IReadOnlyList<string> contactKeys, CancellationToken ct = default) =>
        Task.FromResult(false);

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
            var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");
            if (data.GetArrayLength() == 0)
                return null;

            var row = data[0];
            var spend = row.TryGetProperty("spend", out var s) && decimal.TryParse(s.GetString(), out var sv) ? sv : 0m;
            var impressions = row.TryGetProperty("impressions", out var i) && int.TryParse(i.GetString(), out var iv) ? iv : 0;
            var clicks = row.TryGetProperty("clicks", out var c) && int.TryParse(c.GetString(), out var cv) ? cv : 0;

            var cpl = clicks > 0 ? spend / clicks : 0m;
            var ctr = impressions > 0 ? (decimal)clicks / impressions * 100 : 0m;
            var frequency = 1m;

            return new AdsMetricSnapshot(cpl, frequency, ctr, spend, 0m);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
