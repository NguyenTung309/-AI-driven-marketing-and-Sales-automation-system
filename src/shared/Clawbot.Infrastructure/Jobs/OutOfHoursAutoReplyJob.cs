using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class OutOfHoursAutoReplyJob(
    AppDbContext db,
    IClock clock,
    ILogger<OutOfHoursAutoReplyJob> logger)
{
    private static readonly TimeOnly WorkStart = new(8, 0);
    private static readonly TimeOnly WorkEnd = new(22, 0);
    private static readonly TimeSpan Gmt7Offset = TimeSpan.FromHours(7);
    private const string DefaultReplyText =
        "Cảm ơn bạn đã liên hệ! Hiện tại ngoài giờ làm việc (8:00-22:00). " +
        "Chúng tôi sẽ phản hồi trong giờ làm việc tiếp theo.";

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var recentThreshold = now.AddMinutes(-5);

        var staleConversations = await db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.Status == "open"
                && c.LastMessageAt != null
                && c.LastMessageAt < recentThreshold
                && c.LastMessageAt > now.AddHours(-2))
            .Select(c => new { c.Id, c.TenantId })
            .Take(50)
            .ToListAsync(ct);

        if (staleConversations.Count == 0)
        {
            LogSkipped(logger, "no stale conversations");
            return;
        }

        LogProcessing(logger, staleConversations.Count);
        var settings = await LoadSettingsAsync(staleConversations.Select(c => c.TenantId).Distinct(), ct)
            .ConfigureAwait(false);
        var handled = 0;

        foreach (var stale in staleConversations.DistinctBy(c => c.Id))
        {
            var tenantSettings = settings.GetValueOrDefault(stale.TenantId, OutOfHoursSettings.Default);
            if (!tenantSettings.Enabled || tenantSettings.IsWithinBusinessHours(now))
                continue;

            var hasSystemReply = await db.Messages
                .IgnoreQueryFilters()
                .Where(m => m.ConversationId == stale.Id
                    && m.SenderType == "system"
                    && m.SentAt > now.AddHours(-2))
                .AnyAsync(ct);

            if (hasSystemReply) continue;

            var conv = await db.Conversations.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == stale.Id, ct);
            if (conv is null) continue;

            conv.AppendMessage("out", "system", tenantSettings.ReplyText, "text", now);
            handled++;
        }

        await db.SaveChangesAsync(ct);
        LogCompleted(logger, handled);
    }

    private async Task<IReadOnlyDictionary<Guid, OutOfHoursSettings>> LoadSettingsAsync(
        IEnumerable<Guid> tenantIds,
        CancellationToken ct)
    {
        var ids = tenantIds.ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, OutOfHoursSettings>();

        var configs = await db.AgentConfigs.IgnoreQueryFilters()
            .Where(c => ids.Contains(c.TenantId)
                && c.DeletedAt == null
                && (c.AgentType == "chat" || c.Code == "chat"))
            .Select(c => new { c.TenantId, c.ConfigJson, c.UpdatedAt })
            .ToListAsync(ct);

        return configs
            .GroupBy(c => c.TenantId)
            .ToDictionary(
                g => g.Key,
                g => ParseSettings(g
                    .OrderByDescending(c => c.UpdatedAt)
                    .Select(c => c.ConfigJson)
                    .FirstOrDefault()));
    }

    private static OutOfHoursSettings ParseSettings(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return OutOfHoursSettings.Default;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            if (!doc.RootElement.TryGetProperty("outOfHours", out var section)
                || section.ValueKind != JsonValueKind.Object)
                return OutOfHoursSettings.Default;

            var enabled = !section.TryGetProperty("enabled", out var enabledProp)
                || enabledProp.ValueKind != JsonValueKind.False;
            var start = ReadTime(section, "workStart", WorkStart);
            var end = ReadTime(section, "workEnd", WorkEnd);
            var offset = ReadOffset(section);
            var reply = ReadString(section, "replyText") ?? DefaultReplyText;

            return new OutOfHoursSettings(enabled, start, end, offset, reply);
        }
        catch (JsonException)
        {
            return OutOfHoursSettings.Default;
        }
    }

    private static TimeOnly ReadTime(JsonElement section, string property, TimeOnly fallback)
    {
        var value = ReadString(section, property);
        return TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : fallback;
    }

    private static TimeSpan ReadOffset(JsonElement section)
    {
        if (section.TryGetProperty("timezoneOffsetHours", out var prop) && prop.TryGetDouble(out var hours))
            return TimeSpan.FromHours(hours);

        return Gmt7Offset;
    }

    private static string? ReadString(JsonElement section, string property) =>
        section.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private sealed record OutOfHoursSettings(
        bool Enabled,
        TimeOnly Start,
        TimeOnly End,
        TimeSpan UtcOffset,
        string ReplyText)
    {
        public static OutOfHoursSettings Default { get; } =
            new(true, WorkStart, WorkEnd, Gmt7Offset, DefaultReplyText);

        public bool IsWithinBusinessHours(DateTimeOffset utcNow)
        {
            var localTime = TimeOnly.FromDateTime(utcNow.ToOffset(UtcOffset).DateTime);
            return Start <= End
                ? localTime >= Start && localTime <= End
                : localTime >= Start || localTime <= End;
        }
    }

    [LoggerMessage(EventId = 10001, Level = LogLevel.Debug,
        Message = "OutOfHours job skipped: {Reason}")]
    private static partial void LogSkipped(ILogger logger, string reason);

    [LoggerMessage(EventId = 10002, Level = LogLevel.Information,
        Message = "OutOfHours job processing {Count} stale conversations")]
    private static partial void LogProcessing(ILogger logger, int count);

    [LoggerMessage(EventId = 10003, Level = LogLevel.Information,
        Message = "OutOfHours job completed: {Count} conversations handled")]
    private static partial void LogCompleted(ILogger logger, int count);
}
