namespace Clawbot.Agents.Core.Skills.Content;

public sealed record TrendingHashtag(string Tag, long PostCount, double GrowthRate24h);

public interface IHashtagResearcher : ISkill
{
    Task<IReadOnlyList<TrendingHashtag>> TopAsync(string platform, string region, int limit, CancellationToken ct);
}

// Source: TikTok Creative Center (https://ads.tiktok.com/business/creativecenter) scrape + Google Trends VN.
// Cache 6h in Redis. Block requests outside business hours to avoid scrape ban.
internal sealed class TikTokHashtagResearcher : IHashtagResearcher
{
    public string Name => "hashtag-research-vn";

    public Task<IReadOnlyList<TrendingHashtag>> TopAsync(string platform, string region, int limit, CancellationToken ct) =>
        throw new NotImplementedException("TODO: HttpClient → Creative Center JSON + parse trending list.");
}
