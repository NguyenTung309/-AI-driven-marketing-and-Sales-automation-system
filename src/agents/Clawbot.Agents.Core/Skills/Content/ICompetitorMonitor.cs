using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using AngleSharp;

namespace Clawbot.Agents.Core.Skills.Content;

public sealed record CompetitorPost(string Source, string Url, string Title, string? Snippet, DateTimeOffset PublishedAt);

public interface ICompetitorMonitor : ISkill
{
    Task<IReadOnlyList<CompetitorPost>> FetchSinceAsync(IReadOnlyList<string> sourceUrls, DateTimeOffset since, CancellationToken ct);
}

internal sealed class RssCompetitorMonitor(HttpClient http) : ICompetitorMonitor
{
    public string Name => "competitor-monitor";

    public async Task<IReadOnlyList<CompetitorPost>> FetchSinceAsync(IReadOnlyList<string> sourceUrls, DateTimeOffset since, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sourceUrls);

        var results = new List<CompetitorPost>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var url in sourceUrls)
        {
            try
            {
                var posts = await ParseFeedAsync(url, since, ct).ConfigureAwait(false);
                foreach (var post in posts)
                {
                    if (seen.Add(HashUrl(post.Url)))
                        results.Add(post);
                }
            }
            catch (HttpRequestException)
            {
                // Skip unreachable feeds — don't fail the whole batch
            }
        }

        return results.OrderByDescending(p => p.PublishedAt).ToList();
    }

    private async Task<IReadOnlyList<CompetitorPost>> ParseFeedAsync(string feedUrl, DateTimeOffset since, CancellationToken ct)
    {
        var xml = await http.GetStringAsync(feedUrl, ct).ConfigureAwait(false);
        var doc = XDocument.Parse(xml);
        var root = doc.Root;
        if (root is null) return Array.Empty<CompetitorPost>();

        var ns = root.GetDefaultNamespace();
        var items = root.Descendants(ns + "item");
        if (!items.Any())
            items = root.Descendants("item");

        var posts = new List<CompetitorPost>();
        foreach (var item in items)
        {
            var title = item.Element(ns + "title")?.Value ?? item.Element("title")?.Value ?? "Untitled";
            var link = item.Element(ns + "link")?.Value ?? item.Element("link")?.Value ?? string.Empty;
            var desc = item.Element(ns + "description")?.Value ?? item.Element("description")?.Value;
            var pubDateStr = item.Element(ns + "pubDate")?.Value ?? item.Element("pubDate")?.Value;

            var published = TryParseRfc822Date(pubDateStr, out var parsed) ? parsed : DateTimeOffset.UtcNow;
            if (published < since) continue;

            var source = new Uri(feedUrl).Host;
            posts.Add(new CompetitorPost(source, link.Trim(), title.Trim(), Truncate(desc, 300), published));
        }

        return posts;
    }

    private static string? Truncate(string? text, int maxLen) =>
        text is not null && text.Length > maxLen ? text[..maxLen] + "..." : text;

    private static string HashUrl(string url)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(bytes)[..16];
    }

    private static bool TryParseRfc822Date(string? input, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(input)) return false;

        // Normalize timezone: "+0700" → "+07:00", "+07" → "+07:00"
        var s = input.Trim();
        var lastSpace = s.LastIndexOf(' ');
        if (lastSpace >= 0 && lastSpace < s.Length - 1)
        {
            var tz = s[(lastSpace + 1)..];
            if (tz.Length == 5 && (tz[0] == '+' || tz[0] == '-') && tz[1..3].All(char.IsDigit) && tz[3..5].All(char.IsDigit))
                s = s[..(lastSpace + 1)] + tz[..3] + ":" + tz[3..];
            else if (tz.Length == 3 && (tz[0] == '+' || tz[0] == '-') && tz[1..3].All(char.IsDigit))
                s = s + ":00";
        }

        string[] formats =
        [
            "ddd, dd MMM yyyy HH:mm:ss zzz",
            "ddd, dd MMM yyyy HH:mm zzz",
            "dd MMM yyyy HH:mm:ss zzz",
            "yyyy-MM-ddTHH:mm:sszzz",
            "yyyy-MM-ddTHH:mm:ssZ",
        ];

        return DateTimeOffset.TryParseExact(s, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out result);
    }
}
