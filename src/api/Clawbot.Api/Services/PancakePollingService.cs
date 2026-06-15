using System.Text.Json;
using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Channels;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Demo;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Services;

public sealed partial class PancakePollingService : BackgroundService
{
    private readonly DemoTraceService _traces;
    private readonly DemoRuntimeConfigStore _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PancakePollingService> _log;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    private const string DefaultBaseUrl = "https://pages.fm/api/public_api/v1";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private DateTime _lastPollUtc = DateTime.MinValue;

    public PancakePollingService(
        DemoTraceService traces,
        DemoRuntimeConfigStore config,
        IHttpClientFactory httpFactory,
        ILogger<PancakePollingService> log,
        IServiceScopeFactory scopeFactory)
    {
        _traces = traces;
        _config = config;
        _httpFactory = httpFactory;
        _scopeFactory = scopeFactory;
        _log = log;
    }

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "PancakePollingService started")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Warning, Message = "Pancake poll failed: {Msg}")]
    private static partial void LogPollFailed(ILogger logger, string msg);

    [LoggerMessage(EventId = 5007, Level = LogLevel.Information, Message = "Skipped processed message {MsgId} for conv {ConvId}")]
    private static partial void LogSkippedProcessed(ILogger logger, string msgId, string convId);

    [LoggerMessage(EventId = 5008, Level = LogLevel.Information, Message = "Ingested new message {MsgId} from conv {ConvId}")]
    private static partial void LogProcessedNew(ILogger logger, string msgId, string convId);

    [LoggerMessage(EventId = 5009, Level = LogLevel.Warning, Message = "IngestAsync failed for message {MsgId}: {Ex}")]
    private static partial void LogIngestFailed(ILogger logger, string msgId, string ex);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(_log);
        await Task.Delay(PollInterval, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cfg = _config.Get();
                if (cfg.IsTokenConfigured && cfg.PancakePageId is not null)
                {
                    await PollConversationsAsync(cfg, stoppingToken);
                }
                _lastPollUtc = DateTime.UtcNow;
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                LogPollFailed(_log, ex.Message);
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private async Task PollConversationsAsync(DemoRuntimeConfig cfg, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("Pancake");
        var baseUrl = string.IsNullOrEmpty(cfg.PancakeBaseUrl) ? DefaultBaseUrl : cfg.PancakeBaseUrl;
        var pageId = cfg.PancakePageId!;
        var token = cfg.PancakePageAccessToken!;

        var convUrl = $"https://pages.fm/api/public_api/v2/pages/{pageId}/conversations?page_access_token={token}&per_page=50";
        var convResp = await client.GetAsync(convUrl, ct);
        if (!convResp.IsSuccessStatusCode) return;

        var json = await convResp.Content.ReadAsStringAsync(ct);
        var resp = JsonSerializer.Deserialize<PancakeConversationsResponse>(json, JsonOpts);
        if (resp?.Conversations is null) return;

        foreach (var conv in resp.Conversations)
        {
            if (conv.UpdatedAt <= _lastPollUtc) continue;
            var snippet = conv.Snippet ?? "";
            if (string.IsNullOrWhiteSpace(snippet)) continue;

            // Fetch latest message to get real message_id for dedup
            var msgUrl = $"{baseUrl}/pages/{pageId}/conversations/{conv.Id}/messages?page_access_token={token}&limit=1";
            var msgResp = await client.GetAsync(msgUrl, ct);
            if (!msgResp.IsSuccessStatusCode) continue;
            var msgJson = await msgResp.Content.ReadAsStringAsync(ct);
            var msgData = JsonSerializer.Deserialize<PancakeMessagesResponse>(msgJson, JsonOpts);
            var latestMsg = msgData?.Messages?.FirstOrDefault();
            if (latestMsg?.Id is null) continue;

            var convId = conv.Id ?? "unknown";

            // DB-backed dedup: only process each external_message_id once
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var alreadyProcessed = await db.ProcessedMessages
                .AnyAsync(p => p.Platform == "zalo" && p.ExternalMessageId == latestMsg.Id, ct);

            if (alreadyProcessed)
            {
                LogSkippedProcessed(_log, latestMsg.Id, convId);
                continue;
            }

            // Skip automated/admin messages (only process real customers)
            if (latestMsg.From?.IsAutomated == true) continue;
            if (!string.IsNullOrEmpty(latestMsg.From?.AdminId)) continue;

            // Mark processed immediately to prevent re-processing
            db.ProcessedMessages.Add(new ProcessedMessage("zalo", latestMsg.Id, convId));
            await db.SaveChangesAsync(ct);

            LogProcessedNew(_log, latestMsg.Id, convId);

            // ===== Ingest message to inbox DB =====
            try
            {
                var resolver = scope.ServiceProvider.GetRequiredService<ITenantResolver>();
                var ingestor = scope.ServiceProvider.GetRequiredService<IChannelMessageIngestor>();
                var tenantId = await resolver.ResolveTenantIdAsync(ct);

                var channelMsg = new ChannelMessage(
                    Channel: "zalo",
                    ExternalThreadId: convId,
                    ExternalUserId: latestMsg.From?.Id ?? "unknown",
                    Text: snippet,
                    SentAt: conv.UpdatedAt.HasValue
                        ? new DateTimeOffset(conv.UpdatedAt.Value, TimeSpan.Zero)
                        : DateTimeOffset.UtcNow,
                    Metadata: new Dictionary<string, string>
                    {
                        ["external_message_id"] = latestMsg.Id,
                        ["content_type"] = "text",
                    });

                await ingestor.IngestAsync(tenantId, channelMsg, ct);
            }
            catch (Exception ex)
            {
                LogIngestFailed(_log, latestMsg.Id, ex.Message);
            }

            // Trace the poll step
            var traceId = await _traces.CreateTraceAsync();

            await _traces.AppendStepAsync(traceId, new DemoTraceStep
            {
                Layer = "pancake_poll",
                Status = DemoTraceStepStatus.Success,
                Output = new()
                {
                    ["action"] = "inbound_message",
                    ["platform"] = "zalo",
                    ["text"] = snippet,
                    ["conversation_id"] = convId,
                    ["message_count"] = conv.MessageCount,
                },
            });

            // Resolve auto-reply text from QuickReplyTemplate
            string draft;
            try
            {
                var qrt = await db.QuickReplyTemplates
                    .AsNoTracking()
                    .Where(q => q.Code == "auto_reply")
                    .FirstOrDefaultAsync(ct);
                draft = qrt?.Body ?? "Cáº£m Æ¡n báº¡n Ä‘Ã£ liÃªn há»‡, chÃºng tÃ´i sáº½ pháº£n há»“i sá»›m";
            }
            catch
            {
                draft = "Cáº£m Æ¡n báº¡n Ä‘Ã£ liÃªn há»‡, chÃºng tÃ´i sáº½ pháº£n há»“i sá»›m";
            }

            // Trace agent step
            await _traces.AppendStepAsync(traceId, new DemoTraceStep
            {
                Layer = "agent",
                Status = DemoTraceStepStatus.Success,
                DurationMs = 100 + new Random().Next(50, 300),
                Output = new()
                {
                    ["agent"] = "AutoReplyAgent",
                    ["intent"] = "general",
                    ["confidence"] = 95,
                    ["action"] = "auto_send",
                    ["draftLength"] = draft.Length,
                },
            });

            // Send reply via Pancake API
            if (cfg.IsPageTokenConfigured && !string.IsNullOrEmpty(cfg.PancakePageId) && conv.Id is not null)
            {
                var sendBaseUrl = string.IsNullOrEmpty(cfg.PancakeBaseUrl) ? DefaultBaseUrl : cfg.PancakeBaseUrl;
                var apiUrl = $"{sendBaseUrl}/pages/{cfg.PancakePageId}/conversations/{conv.Id}/messages?page_access_token={cfg.PancakePageAccessToken}";
                var outboundStatus = DemoTraceStepStatus.Success;
                string? outboundReason = null;

                try
                {
                    var sendPayload = new { action = "reply_inbox", message = draft };
                    var jsonContent = new StringContent(
                        JsonSerializer.Serialize(sendPayload, JsonOpts),
                        System.Text.Encoding.UTF8,
                        "application/json");

                    using var httpReq = new HttpRequestMessage(HttpMethod.Post, apiUrl)
                    {
                        Content = jsonContent,
                    };
                    using var apiResp = await client.SendAsync(httpReq, ct);

                    if (!apiResp.IsSuccessStatusCode)
                    {
                        outboundStatus = DemoTraceStepStatus.Failed;
                        outboundReason = "pancake_api_error: " + (int)apiResp.StatusCode;
                    }
                }
                catch (Exception ex)
                {
                    outboundStatus = DemoTraceStepStatus.Failed;
                    outboundReason = "api_exception: " + ex.Message;
                }

                await _traces.AppendStepAsync(traceId, new DemoTraceStep
                {
                    Layer = "outbound",
                    Status = outboundStatus,
                    DurationMs = 200,
                    Reason = outboundReason,
                });
            }
            else
            {
                await _traces.AppendStepAsync(traceId, new DemoTraceStep
                {
                    Layer = "outbound",
                    Status = DemoTraceStepStatus.Skipped,
                    Reason = "page_token_not_configured",
                    Output = new() { ["pageTokenConfigured"] = false, ["suggestedDraft"] = draft },
                });
            }

            await _traces.CompleteTraceAsync(traceId);
        }

        _lastPollUtc = DateTime.UtcNow;
    }
}

public sealed record PancakeConversationsResponse(bool? Success, PancakeConversation[]? Conversations);
public sealed record PancakeMessagesResponse(bool? Success, PancakeMessage[]? Messages);
public sealed record PancakeMessage(string? Id, PancakeMessageSender? From);
public sealed record PancakeMessageSender(string? Id, string? AdminId, bool? IsAutomated);
public sealed record PancakeConversation(
    string? Id, string? Type, string? Snippet, int? MessageCount,
    DateTime? UpdatedAt, DateTime? InsertedAt, string? PageId);

