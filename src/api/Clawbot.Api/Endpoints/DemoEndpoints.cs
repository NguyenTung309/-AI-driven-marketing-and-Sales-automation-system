using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Clawbot.Api.Services;
using Clawbot.SharedKernel.Demo;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Clawbot.Api.Endpoints;

public static partial class DemoEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    [LoggerMessage(EventId = 4001, Level = LogLevel.Error, Message = "Demo webhook processing failed for trace {TraceId}")]
    private static partial void LogProcessingFailed(ILogger logger, Exception ex, string traceId);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Warning, Message = "Failed to log error trace {TraceId}")]
    private static partial void LogErrorTraceFailed(ILogger logger, Exception ex, string traceId);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Warning, Message = "Webhook outbound FAIL url={Url} status={Status} body={Body}")]
    private static partial void LogOutboundWebhookFail(ILogger logger, string url, int status, string body);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Information, Message = "Webhook outbound OK url={Url} status={Status} body={Body}")]
    private static partial void LogOutboundWebhookOk(ILogger logger, string url, int status, string body);

    [LoggerMessage(EventId = 4005, Level = LogLevel.Warning, Message = "Webhook outbound exception url={Url}")]
    private static partial void LogOutboundWebhookException(ILogger logger, Exception ex, string url);

    public static IEndpointRouteBuilder MapDemo(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/demo");

        // Webhook endpoint ? nh?n raw payload t? Pancake
        group.MapPost("/webhook/pancake", HandleWebhookAsync)
             .AllowAnonymous()
             .DisableRateLimiting();
        group.MapGet("/webhook/pancake", VerifyWebhookAsync).AllowAnonymous();

        // Config
        group.MapGet("/config/status", GetConfigStatusAsync);
        group.MapPost("/config/token", SetTokenAsync);
        group.MapPost("/config/webhook-secret", SetWebhookSecretAsync);
        group.MapPost("/config/auto-reply", SetAutoReplyAsync);

        // Trace
        group.MapGet("/traces/{traceId}", GetTraceAsync);
        group.MapGet("/traces/{traceId}/export", ExportTraceAsync);
        // List recent traces for UI history
        group.MapGet("/traces", ListTracesAsync);

        // SSE events
        group.MapGet("/events", StreamEventsAsync);

        // Health
        group.MapGet("/status", () => Results.Ok(new { demoMode = true, now = DateTime.UtcNow }));

        return app;
    }

    // //////////////////////////////////////////////////////////////
    // Webhook handler
    // //////////////////////////////////////////////////////////////

    // Webhook verification ? Pancake/Facebook g?i GET d? verify URL
    private static IResult VerifyWebhookAsync(
        HttpContext ctx)
    {
        var mode = ctx.Request.Query["hub.mode"].FirstOrDefault();
        var challenge = ctx.Request.Query["hub.challenge"].FirstOrDefault();
        var token = ctx.Request.Query["hub.verify_token"].FirstOrDefault();

        if (!string.IsNullOrEmpty(challenge))
            return Results.Text(challenge);

        // Fallback: accept verification
        return Results.Ok(new { status = "verified" });
    }

    private static async Task<IResult> HandleWebhookAsync(
        HttpContext ctx,
        DemoTraceService traces,
        IHttpClientFactory httpClientFactory,
        DemoRuntimeConfigStore configStore,
        IOptions<DemoOptions> opts,
        ILogger<Program> log)
    {
        var options = opts.Value;

        // Webhook demo gửi auto-reply đóng hộp thẳng qua Pancake API, bypass ChatAgent/review-gate —
        // tuyệt đối không để mở anonymous. Chỉ chạy khi Demo:AdminKey được cấu hình VÀ caller gửi đúng
        // X-Admin-Key; chưa cấu hình key thì coi như endpoint không tồn tại. Ingest thật đi đường
        // PancakePollingService, không qua đây.
        var adminKey = ctx.Request.Headers["X-Admin-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(options.AdminKey)
            || !string.Equals(adminKey, options.AdminKey, StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        // Read raw body
        string rawBody;
        using (var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: false))
            rawBody = await reader.ReadToEndAsync();
        // Validate empty body
        if (string.IsNullOrWhiteSpace(rawBody) && !options.Mode)
        {
            return Results.BadRequest(new
            {
                error = "empty_body",
                detail = "Webhook payload is empty. Send a valid Pancake webhook JSON payload with thread_id or conversation_id.",
            });
        }


        // Generate trace ID
        var traceId = ctx.Request.Headers["X-Trace-Id"].FirstOrDefault()
                   ?? $"trc_{Guid.NewGuid():N}"[..20];
        ctx.Response.Headers["X-Trace-Id"] = traceId;

        // HMAC validation
        if (!options.SkipHmac)
        {
            var sigHeader = ctx.Request.Headers["X-Pancake-Signature"].FirstOrDefault();
            var config = configStore.Get();
            if (string.IsNullOrEmpty(config.PancakeWebhookSecret))
            {
                await LogAndReturnTrace(traces, traceId, "gateway", DemoTraceStepStatus.Failed,
                    new { hmacError = "hmac_secret_not_configured" }, 5, log);
                return Results.Json(new { error = "hmac_secret_not_configured" }, statusCode: 401);
            }

            if (string.IsNullOrEmpty(sigHeader))
            {
                await LogAndReturnTrace(traces, traceId, "gateway", DemoTraceStepStatus.Failed,
                    new { hmacError = "missing_signature" }, 5, log);
                return Results.Json(new { error = "invalid_signature" }, statusCode: 401);
            }

            var valid = await Task.Run(() => VerifyHmac(rawBody, sigHeader, config.PancakeWebhookSecret));
            if (!valid)
            {
                await LogAndReturnTrace(traces, traceId, "gateway", DemoTraceStepStatus.Failed,
                    new { hmacError = "signature_mismatch" }, 5, log);
                return Results.Json(new { error = "invalid_signature" }, statusCode: 401);
            }
        }

        var scopeFactory = ctx.RequestServices.GetRequiredService<IServiceScopeFactory>();
        _ = Task.Run(async () => await ProcessWebhookAsync(traceId, rawBody, traces, configStore, opts, log, httpClientFactory, scopeFactory));
        // ACK ngay ? 200 OK
        return Results.Ok(new { traceId, status = "accepted" });
    }

    // //////////////////////////////////////////////////////////////
    // Internal processing (async after ACK)
    // //////////////////////////////////////////////////////////////

    private static async Task ProcessWebhookAsync(
        string traceId, string rawBody,
        DemoTraceService traces, DemoRuntimeConfigStore configStore,
        IOptions<DemoOptions> opts, ILogger<Program> log, IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopeFactory)
    {
        try
        {
            // Truncate at 256KB for storage
            var truncatedBody = rawBody.Length > 256_000
                ? rawBody[..256_000] + "...[TRUNCATED]"
                : rawBody;

            // Attempt to parse basic fields for trace
            object? parsed = null;
            string? platform = null;
            string? text = null;
            string? messageId = null;
            string? conversationId = null;
            try
            {
                parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(rawBody);
                if (parsed is Dictionary<string, object?> dict)
                {
                    // Format 1: Pancake webhook events[] format
                    // {"events":[{"thread_id":"...","text":"...","platform":"zalo",...}]}
                    if (dict.TryGetValue("events", out var evtsObj) && evtsObj is JsonElement evts && evts.ValueKind == JsonValueKind.Array && evts.GetArrayLength() > 0)
                    {
                        var first = evts[0];
                        if (first.TryGetProperty("platform", out var p)) platform = p.GetString() ?? "unknown";
                        if (first.TryGetProperty("text", out var t)) text = t.GetString();
                        if (first.TryGetProperty("message_id", out var mid)) messageId = mid.GetString();
                        if (first.TryGetProperty("thread_id", out var evtTid)) conversationId = evtTid.GetString();
                    }
                    // Format 2: flat payload with thread_id, message, source
                    else
                    {
                        if (dict.TryGetValue("source", out var srcObj) && srcObj is JsonElement src)
                            platform = src.TryGetProperty("platform", out var p) ? p.GetString() ?? "unknown" : "unknown";
                        if (dict.TryGetValue("message", out var msgObj) && msgObj is JsonElement msg)
                        {
                            text = msg.TryGetProperty("text", out var t) ? t.GetString() : null;
                            messageId = msg.TryGetProperty("id", out var id) ? id.GetString() : null;
                            messageId ??= msg.TryGetProperty("msg_id", out var mid) ? mid.GetString() : null;
                        }
                    }
                    // Extract conversation/thread ID ? chung cho ca 2 format
                    conversationId ??= dict.TryGetValue("thread_id", out var tid) && tid is JsonElement te
                        ? te.GetString() : null;
                    conversationId ??= dict.TryGetValue("conversation_id", out var cid) && cid is JsonElement ci
                        ? ci.GetString() : null;
                }
            }
            catch { /* ignore parse errors */ }
            // Demo mode: simulate message when body empty or parse fails to find conversationId
            if (string.IsNullOrEmpty(conversationId) && opts.Value.Mode)
            {
                conversationId = "demo_conv_" + Guid.NewGuid().ToString("N")[..20];
                platform ??= "demo";
                text ??= "Xin ch?o, t?i mu?n tu v?n kh?a h?c";
                messageId ??= "demo_msg_" + Guid.NewGuid().ToString("N")[..16];
                rawBody = System.Text.Json.JsonSerializer.Serialize(new
                {
                    thread_id = conversationId,
                    message = new { text, id = messageId },
                    source = new { platform },
                });
            }


            // Resolve auto-reply text from QuickReplyTemplate
            string draft;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var qrt = await db.QuickReplyTemplates
                    .AsNoTracking()
                    .Where(q => q.Code == "auto_reply")
                    .FirstOrDefaultAsync();
                draft = qrt?.Body ?? "C?m on b?n d? li?n h?, ch?ng t?i s? ph?n h?i s?m";
            }
            catch
            {
                draft = "C?m on b?n d? li?n h?, ch?ng t?i s? ph?n h?i s?m";
            }

            // Create trace
            await traces.CreateTraceAsync(traceId);

            var bodyPreview = $"[REDACTED payload, {rawBody.Length} chars]";
            // Gateway step
            await traces.AppendStepAsync(traceId, new DemoTraceStep
            {
                Layer = "gateway",
                Status = DemoTraceStepStatus.Success,
                DurationMs = 5,
                Output = new() { ["platform"] = platform, ["hmacValid"] = true, ["rawBodyPreview"] = bodyPreview },
            });

            // Ingestor step
            await traces.AppendStepAsync(traceId, new DemoTraceStep
            {
                Layer = "ingestor",
                Status = DemoTraceStepStatus.Success,
                DurationMs = 30,
                Output = new()
                {
                    ["action"] = "upsert_conversation",
                    ["platform"] = platform,
                    ["messageLength"] = text?.Length ?? 0,
                    ["messageId"] = messageId,
                    ["contentTruncated"] = rawBody.Length > 256_000,
                },
            });

            // Outbox step
            await traces.AppendStepAsync(traceId, new DemoTraceStep
            {
                Layer = "outbox",
                Status = DemoTraceStepStatus.Success,
                DurationMs = 10,
                Output = new()
                {
                    ["eventType"] = "inbound_message",
                    ["queue"] = "inbox.ingest",
                    ["idempotencyKey"] = messageId ?? "fallback-auto",
                },
            });

            // Agent step (simulated ? real impl calls gRPC)
            var config = configStore.Get();
            var agentMs = 100 + new Random().Next(50, 300);

            await traces.AppendStepAsync(traceId, new DemoTraceStep
            {
                Layer = "agent",
                Status = DemoTraceStepStatus.Success,
                DurationMs = agentMs,
                Output = new()
                {
                    ["agent"] = "AutoReplyAgent",
                    ["intent"] = "general",
                    ["confidence"] = 95,
                    ["action"] = "auto_send",
                    ["draftLength"] = draft.Length,
                    ["llmCostUsd"] = 0.0015,
                },
            });

            // Outbound step ? gui tin nhan qua Pancake API
            if (config.IsPageTokenConfigured)
            {
                var sw = Stopwatch.StartNew();
                var stepStatus = DemoTraceStepStatus.Success;
                string? stepReason = null;
                var stepOutput = new Dictionary<string, object?>
                {
                    ["action"] = "send_message",
                    ["pageTokenConfigured"] = true,
                    ["conversationId"] = conversationId,
                    ["platform"] = platform,
                };

                if (!string.IsNullOrEmpty(conversationId) && !string.IsNullOrEmpty(config.PancakePageId))
                {
                    // Demo simulated conversation: skip actual API call, mark as success
                    if (conversationId.StartsWith("demo_", StringComparison.Ordinal))
                    {
                        sw.Stop();
                        stepStatus = DemoTraceStepStatus.Success;
                        stepReason = null;
                        stepOutput["demoMode"] = true;
                        stepOutput["simulated"] = true;
                        stepOutput["simulatedConversationId"] = conversationId;
                        stepOutput["simulatedDraft"] = draft;
                    }
                    else
                    {
                        var sendBaseUrl = (string.IsNullOrEmpty(config.PancakeBaseUrl) ?
                            "https://pages.fm/api/public_api/v1" : config.PancakeBaseUrl)
                            .Replace("/v2/", "/v1/")
                            .Replace("/v2", "/v1");
                        // Thread ID c? th? ? d?ng composite page_id:thread_id -> c?n t?ch
                        var threadId = conversationId;
                        var colonIdx = threadId?.IndexOf(':', StringComparison.Ordinal) ?? -1;
                        if (colonIdx > 0 && threadId != null) threadId = threadId.Substring(colonIdx + 1);
                        var apiUrl = $"{sendBaseUrl}/pages/{config.PancakePageId}/conversations/{threadId}/messages?page_access_token={config.PancakePageAccessToken}";

                    try
                    {
                        var sendPayload = new { action = "reply_inbox", message = draft };
                        var jsonContent = new StringContent(
                            JsonSerializer.Serialize(sendPayload, JsonOpts),
                            Encoding.UTF8,
                            "application/json");

                        var httpClient = httpClientFactory.CreateClient("Pancake");
                        var apiResp = await httpClient.PostAsync(apiUrl, jsonContent);
                        sw.Stop();

                        var respBody = await apiResp.Content.ReadAsStringAsync();

                        stepOutput["url"] = apiUrl.Replace(config.PancakePageAccessToken!, "...");
                        stepOutput["statusCode"] = (int)apiResp.StatusCode;
                        stepOutput["responsePreview"] = respBody.Length > 500
                            ? respBody[..500] + "..."
                            : respBody;

                        if (!apiResp.IsSuccessStatusCode)
                        {
                            stepStatus = DemoTraceStepStatus.Failed;
                            stepReason = $"pancake_api_error: {(int)apiResp.StatusCode}";
                            #pragma warning disable CA1873
                        LogOutboundWebhookFail(log, apiUrl.Replace(config.PancakePageAccessToken!, "..."), (int)apiResp.StatusCode, respBody);
#pragma warning restore CA1873
                        }
                        else
                        {
                            #pragma warning disable CA1873
                        LogOutboundWebhookOk(log, apiUrl.Replace(config.PancakePageAccessToken!, "..."), (int)apiResp.StatusCode, respBody);
#pragma warning restore CA1873
                        }
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        stepStatus = DemoTraceStepStatus.Failed;
                        stepReason = $"api_exception: {ex.Message}";
                        stepOutput["error"] = ex.Message;
                        #pragma warning disable CA1873
                        LogOutboundWebhookException(log, ex, apiUrl.Replace(config.PancakePageAccessToken!, "..."));
#pragma warning restore CA1873
                    }
                    }
                }
                else
                {
                    sw.Stop();
                    stepStatus = DemoTraceStepStatus.Skipped;
                    stepReason = string.IsNullOrEmpty(conversationId)
                        ? "missing_conversation_id"
                        : "missing_page_id";
                    stepOutput["pageId"] = config.PancakePageId;
                }

                await traces.AppendStepAsync(traceId, new DemoTraceStep
                {
                    Layer = "outbound",
                    Status = stepStatus,
                    DurationMs = sw.ElapsedMilliseconds,
                    Reason = stepReason,
                    Output = stepOutput,
                });
            }
            else
            {
                await traces.AppendStepAsync(traceId, new DemoTraceStep
                {
                    Layer = "outbound",
                    Status = DemoTraceStepStatus.Skipped,
                    Reason = "page_token_not_configured",
                    Output = new()
                    {
                        ["pageTokenConfigured"] = false,
                        ["suggestedDraft"] = draft,
                    },
                });
            }

            await traces.CompleteTraceAsync(traceId);
        }
        catch (Exception ex)
        {
            LogProcessingFailed(log, ex, traceId);
            try
            {
                await traces.AbandonTraceAsync(traceId, $"processing_error: {ex.Message}");
            }
            catch { /* ignore */ }
        }
    }

    // //////////////////////////////////////////////////////////////
    // Config endpoints
    // //////////////////////////////////////////////////////////////

    private static IResult GetConfigStatusAsync(
        DemoRuntimeConfigStore store, IOptions<DemoOptions> opts)
    {
        var c = store.Get();
        return Results.Ok(new
        {
            tokenConfigured = c.IsTokenConfigured,
            pageTokenConfigured = c.IsPageTokenConfigured,
            secretConfigured = c.IsSecretConfigured,
            mode = opts.Value.Mode,
            maskPii = opts.Value.MaskPii,
            skipHmac = opts.Value.SkipHmac,
            adminKeyConfigured = !string.IsNullOrEmpty(opts.Value.AdminKey),
            watchdogInterval = opts.Value.WatchdogIntervalSeconds,
            sseReplayCount = opts.Value.SseReplayCount,
            traceTtlMinutes = opts.Value.EffectiveTtlMinutes,
            pageId = c.PancakePageId,
            baseUrl = c.PancakeBaseUrl,
            autoReplyText = c.AutoReplyText ?? "(using default)",
        });
    }

    private static IResult SetTokenAsync(
        SetTokenRequest req, DemoRuntimeConfigStore store)
    {
        store.UpdateToken(req.Token);
        if (req.PageAccessToken is not null) store.UpdatePageAccessToken(req.PageAccessToken);
        if (req.PageId is not null) store.UpdatePageId(req.PageId);
        if (req.BaseUrl is not null) store.UpdateBaseUrl(req.BaseUrl);
        return Results.Ok(new { status = "pancake_config_updated" });
    }
    private static IResult SetWebhookSecretAsync(
        SetTokenRequest req, DemoRuntimeConfigStore store)
    {
        store.UpdateSecret(req.Secret);
        return Results.Ok(new { status = "secret_updated" });
    }

    private static IResult SetAutoReplyAsync(
        SetAutoReplyRequest req, DemoRuntimeConfigStore store)
    {
        store.UpdateAutoReplyText(req.Text);
        return Results.Ok(new { status = "auto_reply_updated", text = req.Text });
    }

    // Trace endpoints
    // //////////////////////////////////////////////////////////////

    private static async Task<IResult> GetTraceAsync(
        string traceId, DemoTraceService traces)
    {
        var trace = await traces.GetTraceAsync(traceId);
        if (trace is null)
            return Results.NotFound(new { error = "trace_expired",
                detail = "Trace da qua thoi gian va bi xoa khoi Redis" });
        return Results.Ok(trace);
    }

    private static async Task<IResult> ExportTraceAsync(
        string traceId, DemoTraceService traces,
        IOptions<DemoOptions> opts)
    {
        var json = await traces.GetTraceExportJsonAsync(traceId);
        if (json == "null")
            return Results.NotFound(new { error = "trace_expired" });
        return Results.Text(json, "application/json");
    }

    private static async Task<IResult> ListTracesAsync(
        DemoTraceService traces, IOptions<DemoOptions> opts)
    {
        var list = await traces.GetRecentTracesAsync(20);
        return Results.Ok(list);
    }

    // //////////////////////////////////////////////////////////////
    // SSE events endpoint
    // //////////////////////////////////////////////////////////////

    private static async Task StreamEventsAsync(
        HttpContext ctx, DemoTraceService traces, IOptions<DemoOptions> opts,
        CancellationToken ct)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        ctx.Response.Headers["Connection"] = "keep-alive";

        var lastEventId = ctx.Request.Headers["Last-Event-ID"].FirstOrDefault();
        var replayCount = opts.Value.SseReplayCount;

        await foreach (var evt in traces.SubscribeEventsAsync(lastEventId, replayCount, ct))
        {
            await ctx.Response.WriteAsync(evt, ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }

    // //////////////////////////////////////////////////////////////
    // HMAC verification
    // //////////////////////////////////////////////////////////////

    private static bool VerifyHmac(string body, string signatureHeader, string secret)
    {
        // Expected format: sha256=<hex>
        var hex = signatureHeader;
        if (hex.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            hex = hex["sha256=".Length..];

        if (hex.Length != 64) return false; // SHA256 hex length

        var key = Encoding.UTF8.GetBytes(secret);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var computed = HMACSHA256.HashData(key, bodyBytes);
        var computedHex = Convert.ToHexString(computed).ToLowerInvariant();

        return string.Equals(hex, computedHex, StringComparison.OrdinalIgnoreCase);
    }

    // //////////////////////////////////////////////////////////////
    // Helpers
    // //////////////////////////////////////////////////////////////

    private static async Task LogAndReturnTrace(
        DemoTraceService traces, string traceId, string layer,
        DemoTraceStepStatus status, object output,
        int durationMs, ILogger log)
    {
        try
        {
            await traces.CreateTraceAsync(traceId);
            await traces.AppendStepAsync(traceId, new DemoTraceStep
            {
                Layer = layer,
                Status = status,
                DurationMs = durationMs,
                Output = new() { ["error"] = output },
            });
            await traces.CompleteTraceAsync(traceId);
        }
        catch (Exception ex)
        {
            LogErrorTraceFailed(log, ex, traceId);
        }
    }
}

// //////////////////////////////////////////////////////////////////
// Request DTOs
// //////////////////////////////////////////////////////////////////

public sealed record SetTokenRequest(string? Token, string? Secret, string? PageId, string? BaseUrl, string? PageAccessToken);
public sealed record SetAutoReplyRequest(string? Text);
