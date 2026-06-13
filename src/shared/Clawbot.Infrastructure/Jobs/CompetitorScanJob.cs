using System.Security.Cryptography;
using System.Text;
using Clawbot.Agents.Core.Skills.Content;
using Clawbot.Domain.Competitors;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DomainPost = Clawbot.Domain.Competitors.CompetitorPost;
using ScanPost = Clawbot.Agents.Core.Skills.Content.CompetitorPost;

namespace Clawbot.Infrastructure.Jobs;

// Research-2: daily scan of admin-configured competitor feeds. Activates the previously-orphaned
// RssCompetitorMonitor: fetch new posts per source, persist (deduped by source+hash), and alert
// the tenant when a competitor publishes something new.
public sealed partial class CompetitorScanJob(
    AppDbContext db,
    ICompetitorMonitor monitor,
    IClock clock,
    INotificationPublisher publisher,
    ILogger<CompetitorScanJob> logger)
{
    private static readonly TimeSpan DefaultLookback = TimeSpan.FromDays(7);
    private const int MaxSourcesPerRun = 200;

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        var sources = await db.CompetitorSources.IgnoreQueryFilters()
            .Where(s => s.IsActive && s.DeletedAt == null)
            .OrderBy(s => s.LastScannedAt)
            .Take(MaxSourcesPerRun)
            .ToListAsync(ct).ConfigureAwait(false);

        if (sources.Count == 0)
        {
            LogSkipped(logger, "no active competitor sources");
            return;
        }

        var newPostsByTenant = new Dictionary<Guid, int>();

        foreach (var source in sources)
        {
            var since = source.LastScannedAt ?? now - DefaultLookback;
            IReadOnlyList<ScanPost> fetched;
            try
            {
                fetched = await monitor.FetchSinceAsync([source.Url], since, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogSourceFailed(logger, ex, source.Id, source.Url);
                source.MarkScanned(now);
                continue;
            }

            foreach (var post in fetched)
            {
                var hash = Hash(post.Url);
                var exists = await db.CompetitorPosts.IgnoreQueryFilters()
                    .AnyAsync(p => p.SourceId == source.Id && p.ContentHash == hash, ct).ConfigureAwait(false);
                if (exists) continue;

                db.CompetitorPosts.Add(DomainPost.Create(
                    source.TenantId, source.Id, post.Url, Truncate(post.Title, 512) ?? string.Empty,
                    Truncate(post.Snippet, 1024), post.PublishedAt, now, hash));

                newPostsByTenant[source.TenantId] = newPostsByTenant.GetValueOrDefault(source.TenantId) + 1;
            }

            source.MarkScanned(now);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        foreach (var (tenantId, count) in newPostsByTenant.Where(kv => kv.Value > 0))
        {
            await publisher.PublishAsync(new NotificationRequest(
                tenantId, null, "competitor", "Đối thủ có hoạt động mới",
                Severity: "info",
                Body: $"Phát hiện {count} bài/chiến dịch mới từ đối thủ — xem mục Theo dõi đối thủ.",
                Link: "/competitors"), ct).ConfigureAwait(false);
        }

        LogCompleted(logger, sources.Count, newPostsByTenant.Values.Sum());
    }

    private static string Hash(string url)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(bytes)[..32];
    }

    private static string? Truncate(string? text, int maxLen) =>
        text is not null && text.Length > maxLen ? text[..maxLen] : text;

    [LoggerMessage(EventId = 13101, Level = LogLevel.Debug, Message = "CompetitorScan skipped: {Reason}")]
    private static partial void LogSkipped(ILogger logger, string reason);

    [LoggerMessage(EventId = 13102, Level = LogLevel.Warning, Message = "CompetitorScan source {SourceId} ({Url}) failed")]
    private static partial void LogSourceFailed(ILogger logger, Exception ex, Guid sourceId, string url);

    [LoggerMessage(EventId = 13103, Level = LogLevel.Information, Message = "CompetitorScan done: {Sources} sources, {NewPosts} new posts")]
    private static partial void LogCompleted(ILogger logger, int sources, int newPosts);
}
