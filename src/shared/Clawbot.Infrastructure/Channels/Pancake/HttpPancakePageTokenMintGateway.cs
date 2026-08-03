using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Channels.Pancake;

// HTTP gateway that mints a page access token from a Pancake user access token.
// Verified endpoint (SPEC-16 §5.1): POST {userApiBase}/pages/{pageId}/generate_page_access_token?access_token={user}
// returns the new page token. Minting invalidates the previous page token for that page.
public interface IPageTokenMintGateway
{
    Task<string> MintAsync(string userAccessToken, string pageId, CancellationToken ct = default);
}

// SPEC-16 Module M-3/M-4: lists a user's Pancake pages (GET {userApiBase}/pages?access_token={user}).
// The admin connect flow uses this to show pages for selection before minting per-page tokens.
public interface IPageListGateway
{
    Task<IReadOnlyList<PancakePageSummary>> ListAsync(string userAccessToken, CancellationToken ct = default);
}

public sealed record PancakePageSummary(string PageId, string Name, string Platform);

public sealed partial class HttpPancakePageTokenMintGateway(
    HttpClient http,
    PancakeUserApiOptions options,
    ILogger<HttpPancakePageTokenMintGateway> logger) : IPageTokenMintGateway, IPageListGateway
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = http;
    private readonly PancakeUserApiOptions _options = options;
    private readonly ILogger<HttpPancakePageTokenMintGateway> _logger = logger;

    public async Task<string> MintAsync(string userAccessToken, string pageId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(userAccessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);

        // EARS[WHEN minting a page token THE SYSTEM SHALL call the Pancake user API with the user access token and
        // return the new page token, refusing to proceed if the response carries no token]
        var url = $"{_options.BaseUrl.TrimEnd('/')}/pages/{Uri.EscapeDataString(pageId)}/generate_page_access_token?access_token={Uri.EscapeDataString(userAccessToken)}";
        using var resp = await _http.PostAsync(url, content: null, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(JsonOpts, ct).ConfigureAwait(false);
        if (doc is null)
            throw new InvalidOperationException("Pancake mint response was empty.");

        // ponytail: accept either field name the docs have used across revisions; fail loud if neither present.
        var token = doc.RootElement.TryGetProperty("access_token", out var a)
            ? a.GetString()
            : doc.RootElement.TryGetProperty("page_access_token", out var p)
                ? p.GetString()
                : null;
        if (string.IsNullOrWhiteSpace(token))
        {
            LogMintNoToken(_logger, pageId);
            throw new InvalidOperationException($"Pancake mint response for page '{pageId}' did not include an access token.");
        }
        return token!;
    }

    // EARS[WHEN listing pages THE SYSTEM SHALL call the Pancake user API with the user access token and return
    // the page summaries (id/name/platform) for admin selection]
    public async Task<IReadOnlyList<PancakePageSummary>> ListAsync(string userAccessToken, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(userAccessToken);
        var url = $"{_options.BaseUrl.TrimEnd('/')}/pages?access_token={Uri.EscapeDataString(userAccessToken)}";
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>(JsonOpts, ct).ConfigureAwait(false);
        if (doc is null) return [];

        // ponytail: Pancake returns { data: [ { id, name, platform?, ... } ] } or a top-level array across revisions.
        var pagesEl = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement
            : doc.RootElement.TryGetProperty("data", out var d) ? d : default;
        if (pagesEl.ValueKind != JsonValueKind.Array) return [];

        var result = new List<PancakePageSummary>();
        foreach (var el in pagesEl.EnumerateArray())
        {
            var id = el.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) continue;
            var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
            var platform = el.TryGetProperty("platform", out var p) ? p.GetString() ?? string.Empty : string.Empty;
            result.Add(new PancakePageSummary(id!, name, platform));
        }
        return result;
    }

    [LoggerMessage(EventId = 6010, Level = LogLevel.Error, Message = "Pancake mint for page {pageId} returned no access token")]
    private static partial void LogMintNoToken(ILogger logger, string pageId);
}

public sealed class PancakeUserApiOptions
{
    public const string SectionName = "Channels:Pancake:UserApi";
    public string BaseUrl { get; init; } = "https://pages.fm/api/v1";
}
