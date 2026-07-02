using System.Text.Json;
using System.Text.Json.Serialization;
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

    [LoggerMessage(EventId = 5010, Level = LogLevel.Information, Message = "Polling: found {Count} inboxes with tokens")]
    private static partial void LogPollCount(ILogger logger, int Count);

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
        var baseUrl = string.IsNullOrEmpty(cfg.PancakeBaseUrl) ? DefaultBaseUrl : cfg.PancakeBaseUrl;
        var client = _httpFactory.CreateClient("Pancake");

        // 1. Poll all inboxes with tokens from DB
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Clawbot.Infrastructure.Persistence.AppDbContext>();
            var inboxes = await db.Inboxes
                .IgnoreQueryFilters()
                .Where(i => i.EncryptedAccessToken != null && i.IsActive)
                .ToListAsync(ct);

            LogPollCount(_log, inboxes.Count);

            foreach (var inbox in inboxes)
            {
                var token = inbox.EncryptedAccessToken;
                if (string.IsNullOrEmpty(token)) continue;

                await PollPageAsync(client, baseUrl, inbox.ExternalPageId, token, ct);
            }
        }
        catch (Exception ex)
        {
            #pragma warning disable CA1848
            _log.LogWarning(ex, "Failed to poll inboxes from DB, falling back to env-var page");
        }

        // 2. Fallback: poll the env-var page (demo mode)
        if (!string.IsNullOrEmpty(cfg.PancakePageId) && !string.IsNullOrEmpty(cfg.PancakePageAccessToken))
        {
            await PollPageAsync(client, baseUrl, cfg.PancakePageId, cfg.PancakePageAccessToken, ct);
        }
    }

    private async Task PollPageAsync(HttpClient client, string baseUrl, string pageId, string token, CancellationToken ct)
    {
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

            var msgUrl = $"{baseUrl}/pages/{pageId}/conversations/{conv.Id}/messages?page_access_token={token}&limit=50";
            var msgResp = await client.GetAsync(msgUrl, ct);
            if (!msgResp.IsSuccessStatusCode) continue;
            var msgJson = await msgResp.Content.ReadAsStringAsync(ct);
            var msgData = JsonSerializer.Deserialize<PancakeMessagesResponse>(msgJson, JsonOpts);
            var latestMsg = msgData?.Messages?.FirstOrDefault();
            if (latestMsg?.Id is null) continue;

            var convId = conv.Id ?? "unknown";

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Clawbot.Infrastructure.Persistence.AppDbContext>();

            var alreadyProcessed = await db.ProcessedMessages
                .AnyAsync(p => p.Platform == "zalo" && p.ExternalMessageId == latestMsg.Id, ct);
            if (alreadyProcessed) { LogSkippedProcessed(_log, latestMsg.Id, convId); continue; }

            // ponytail: was skipping admin/automated; now ingest all for correct backfill

            db.ProcessedMessages.Add(new ProcessedMessage("zalo", latestMsg.Id, convId));
            await db.SaveChangesAsync(ct);
            LogProcessedNew(_log, latestMsg.Id, convId);

            try
            {
                var resolver = scope.ServiceProvider.GetRequiredService<Clawbot.SharedKernel.Multitenancy.ITenantResolver>();
                var ingestor = scope.ServiceProvider.GetRequiredService<Clawbot.Infrastructure.Channels.IChannelMessageIngestor>();
                var tenantId = await resolver.ResolveTenantIdAsync(ct);

                                var metadata = new Dictionary<string, string>
                {
                    ["external_message_id"] = latestMsg.Id,
                    ["content_type"] = "text",
                };

                // Per-message sender info
                if (latestMsg.From != null)
                {
                    if (!string.IsNullOrEmpty(latestMsg.From.Name)) metadata["sender_name"] = latestMsg.From.Name;
                    if (!string.IsNullOrEmpty(latestMsg.From.AvatarUrl)) metadata["sender_avatar_url"] = latestMsg.From.AvatarUrl;
                    metadata["sender_id"] = latestMsg.From.Id ?? "";
                }

                if (conv.From != null)
                {
                    if (!string.IsNullOrEmpty(conv.From.Name) && !metadata.ContainsKey("sender_name")) metadata["sender_name"] = conv.From.Name;
                    if (!string.IsNullOrEmpty(conv.From.AvatarUrl) && !metadata.ContainsKey("sender_avatar_url")) metadata["sender_avatar_url"] = conv.From.AvatarUrl;
                    if (conv.From.IsGroup == true) metadata["is_group"] = "true";
                    metadata["from_id"] = conv.From.Id ?? "";
                }
                if (conv.LastSentBy != null)
                {
                    metadata["sender_id"] = conv.LastSentBy.Id ?? "";
                }
                if (!string.IsNullOrEmpty(conv.PageId)) metadata["page_id"] = conv.PageId;

                // Parse attachments for rich content
                string text = snippet;
                if (latestMsg.Attachments != null && latestMsg.Attachments.Count > 0)
                {
                    var att = latestMsg.Attachments[0];
                    switch (att.Type)
                    {
                        case "photo":
                            metadata["content_type"] = "photo";
                            text = att.Url ?? "";
                            break;
                        case "sticker":
                            metadata["content_type"] = "sticker";
                            text = att.Url ?? "";
                            break;
                        case "document":
                            metadata["content_type"] = "document";
                            text = att.Name ?? "Tai lieu";
                            if (!string.IsNullOrEmpty(att.Url)) metadata["attachment_url"] = att.Url;
                            break;
                        case "pzl_chat_recommended":
                            metadata["content_type"] = "call_missed";
                            text = "Cuoc goi nhlo";
                            break;
                    }
                }

                var channelMsg = new Clawbot.SharedKernel.Channels.ChannelMessage(
                    Channel: "zalo", ExternalThreadId: string.IsNullOrEmpty(conv.PageId) ? convId : $"{conv.PageId}:{convId}",
                    ExternalUserId: conv.From?.Id ?? latestMsg.From?.Id ?? "unknown", Text: text,
                    SentAt: conv.UpdatedAt.HasValue ? new DateTimeOffset(conv.UpdatedAt.Value, TimeSpan.Zero) : DateTimeOffset.UtcNow,
                    Metadata: metadata);
                await ingestor.IngestAsync(tenantId, channelMsg, ct);
            }
            catch (Exception ex) { LogIngestFailed(_log, latestMsg.Id, ex.Message); }

            var traceId = await _traces.CreateTraceAsync();
            await _traces.AppendStepAsync(traceId, new DemoTraceStep
            {
                Layer = "pancake_poll", Status = DemoTraceStepStatus.Success,
                Output = new() { ["action"] = "inbound_message", ["platform"] = "zalo", ["text"] = snippet, ["conversation_id"] = convId, ["message_count"] = conv.MessageCount },
            });
            await _traces.CompleteTraceAsync(traceId);
        }
    }
public sealed record PancakeConversationsResponse(bool? Success, PancakeConversation[]? Conversations);
public sealed record PancakeMessagesResponse(bool? Success, PancakeMessage[]? Messages);
public sealed record PancakeMessage(
    string? Id,
    string? Message,
    PancakeMessageSender? From,
    IReadOnlyList<PancakeAttachment>? Attachments);
public sealed record PancakeMessageSender(
    string? Id,
    string? Name,
    string? AvatarUrl,
    bool? IsGroup,
    string? AdminId,
    bool? IsAutomated);
public sealed record PancakeAttachment(
    string? Type,
    string? Url,
    string? OriginUrl,
    string? Name,
    string? MimeType,
    PancakeImageData? ImageData);
public sealed record PancakeImageData(int? Width, int? Height);

/// <summary>
/// The other party in the conversation (customer contact or group info).
/// </summary>
public sealed record PancakeFrom(
    string? Id,
    string? Name,
    string? AvatarUrl,
    bool? IsGroup);

/// <summary>
/// The user who sent the last message in this conversation.
/// When LastSentBy.Id == PageId, the sender is the page owner/admin.
/// </summary>
public sealed record PancakeLastSentBy(
    string? Id,
    string? Name,
    string? DisplayName,
    string? AvatarUrl,
    string? AdminName);

/// <summary>
/// A customer/participant in the conversation (used for group chats).
/// </summary>
public sealed record PancakeCustomer(
    string? Id,
    string? Name,
    string? AvatarUrl,
    string? FbId);

public sealed record PancakeConversation(
    string? Id, string? Type, string? Snippet, int? MessageCount,
    DateTime? UpdatedAt, DateTime? InsertedAt, string? PageId,
    PancakeFrom? From, PancakeLastSentBy? LastSentBy,
    IReadOnlyList<PancakeCustomer>? Customers);
}


