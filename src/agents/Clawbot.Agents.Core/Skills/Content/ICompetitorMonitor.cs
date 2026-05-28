namespace Clawbot.Agents.Core.Skills.Content;

public sealed record CompetitorPost(string Source, string Url, string Title, string? Snippet, DateTimeOffset PublishedAt);

public interface ICompetitorMonitor : ISkill
{
    Task<IReadOnlyList<CompetitorPost>> FetchSinceAsync(IReadOnlyList<string> sourceUrls, DateTimeOffset since, CancellationToken ct);
}

// Source: RSS feeds + scheduled HTML scrape (Polly retry + ETag).
// Alt SaaS: Brand24 / Mention.com if budget allows.
internal sealed class RssCompetitorMonitor : ICompetitorMonitor
{
    public string Name => "competitor-monitor";

    public Task<IReadOnlyList<CompetitorPost>> FetchSinceAsync(IReadOnlyList<string> sourceUrls, DateTimeOffset since, CancellationToken ct) =>
        throw new NotImplementedException("TODO: parse RSS via System.ServiceModel.Syndication + dedupe by URL hash.");
}
