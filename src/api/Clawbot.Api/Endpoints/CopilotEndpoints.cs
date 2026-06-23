using Clawbot.Api.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using System;

namespace Clawbot.Api.Endpoints;

public sealed record CopilotSuggestRequest(string CurrentDraft, int DraftVersion);
public sealed record CopilotSuggestResponse(string? Suggestion, int DraftVersion);

public sealed record CopilotSummarizeRequest(string? Focus);
public sealed record CopilotSummarizeResponse(string Summary);

public static class CopilotEndpoints
{
    public const string CopilotPolicy = "copilot:suggest";
    private const int CopilotPermitLimit = 30;
    private static readonly TimeSpan CopilotWindow = TimeSpan.FromMinutes(1);

    public static IEndpointRouteBuilder MapCopilot(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/inbox/conversations/{id:guid}/copilot")
            .RequireRateLimiting(CopilotPolicy)
            .RequirePermission("conversations:read");

        grp.MapPost("/suggest", SuggestAsync);
        grp.MapPost("/summarize", SummarizeAsync);

        return app;
    }

    public static IServiceCollection AddCopilotRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(CopilotPolicy, ctx =>
            {
                var user = ctx.User?.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon";
                return RateLimitPartition.GetFixedWindowLimiter(user, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = CopilotPermitLimit,
                    Window = CopilotWindow,
                    QueueLimit = 5,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                });
            });
        });
        return services;
    }

    private static async Task<IResult> SuggestAsync(
        Guid id, CopilotSuggestRequest req,
        AppDbContext db, ITenantAccessor tenants,
        IPiiRedactor pii, IClock clock,
        CancellationToken ct)
    {
        var tenant = tenants.Require();

        if (string.IsNullOrWhiteSpace(req.CurrentDraft) || req.CurrentDraft.Length < 3 || req.CurrentDraft.Length > 200)
            return Results.Ok(new CopilotSuggestResponse(null, req.DraftVersion));

        var conv = await db.Conversations.AsNoTracking()
            .Include(c => c.Messages.OrderByDescending(m => m.SentAt).Take(20))
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conv is null) return Results.NotFound();

        var redactedDraft = (await pii.RedactAsync(req.CurrentDraft, ct)).RedactedText;
        var historyLines = new List<string>();
        foreach (var m in conv.Messages.Take(10).Reverse())
            historyLines.Add((await pii.RedactAsync(m.Content, ct)).RedactedText);
        var historyText = string.Join("\n", historyLines);

        var suggestion = GenerateSimpleSuggestion(redactedDraft, historyText);
        return Results.Ok(new CopilotSuggestResponse(suggestion, req.DraftVersion));
    }

    private static async Task<IResult> SummarizeAsync(
        Guid id, CopilotSummarizeRequest req,
        AppDbContext db, ITenantAccessor tenants,
        IPiiRedactor pii, IClock clock,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var messages = await db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == id && m.TenantId == tenant.TenantId)
            .OrderBy(m => m.SentAt)
            .Take(100)
            .Select(m => m.Content)
            .ToListAsync(ct);

        if (messages.Count == 0)
            return Results.Ok(new CopilotSummarizeResponse("Khong co tin nhan de tom tat."));

        var redactedTexts = new List<string>();
        foreach (var content in messages)
            redactedTexts.Add((await pii.RedactAsync(content, ct)).RedactedText);

        var summary = GenerateSimpleSummary(redactedTexts);
        return Results.Ok(new CopilotSummarizeResponse(summary));
    }

    private static string? GenerateSimpleSuggestion(string draft, string context)
    {
        if (draft.EndsWith('?'))
            return " Toi se giup ban kiem tra thong tin nay.";
        if (draft.Contains("gia", StringComparison.OrdinalIgnoreCase) || draft.Contains("bao gia", StringComparison.OrdinalIgnoreCase))
            return " cua san pham. De toi gui bao gia chi tiet cho ban.";
        if (draft.Contains("cam on", StringComparison.OrdinalIgnoreCase) || draft.Contains("thanks", StringComparison.OrdinalIgnoreCase))
            return " da lien he. Neu co thac mac gi khac, dung ngai chia se nhe.";
        return null;
    }

    private static string GenerateSimpleSummary(List<string> lines)
    {
        if (lines.Count == 0) return "Chua co tin nhan.";
        return $"Cuoc hoi thoai co {lines.Count} tin nhan.";
    }
}