using System.Globalization;
using System.Text;
using System.Text.Json;
using Clawbot.Agents.Core.Orchestrator;

namespace Clawbot.AgentService.Services;

// web.search: tra cuu web qua SearXNG self-host (docker, khong can API key) cho agent
// can thong tin cong khai (gia thi truong, doi thu, tin tuc). Cau hinh Searxng:BaseUrl
// (env Searxng__BaseUrl); thieu cau hinh tool tra loi ro rang thay vi nem exception.
public sealed partial class WebSearchTool(
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<WebSearchTool> logger) : IAgentTool
{
    public const string HttpClientName = "Searxng";

    private const int DefaultMaxResults = 5;
    private const int MaxAllowedResults = 10;
    private const int MaxSnippetChars = 400;

    public string Name => "web.search";
    public string Description => "Tìm kiếm web (SearXNG self-host): tra cứu thông tin công khai như giá thị trường, đối thủ, tin tức.";
    public string InputSchemaJson =>
        """{"type":"object","properties":{"query":{"type":"string","description":"Từ khóa tìm kiếm"},"max_results":{"type":"string","description":"Số kết quả tối đa 1-10, mặc định 5"}},"required":["query"]}""";
    public string RequiredPermission => "";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;

    public async Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, ToolContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(ctx);

        if (!args.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
            return ToolResult.Fail("missing_query: cần tham số 'query'.");

        var baseUrl = config["Searxng:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            return ToolResult.Fail("web_search_not_configured: đặt Searxng:BaseUrl (env Searxng__BaseUrl) trỏ tới SearXNG.");

        if (ctx.DryRun)
            return ToolResult.Ok($"[dry-run] would search web for: {query}");

        var max = DefaultMaxResults;
        if (args.TryGetValue("max_results", out var rawMax)
            && int.TryParse(rawMax, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed is > 0 and <= MaxAllowedResults)
        {
            max = parsed;
        }

        var url = new Uri($"{baseUrl.TrimEnd('/')}/search?q={Uri.EscapeDataString(query)}&format=json&language=vi");
        try
        {
            var client = httpFactory.CreateClient(HttpClientName);
            using var resp = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return ToolResult.Fail($"web_search_http_{(int)resp.StatusCode}: SearXNG trả lỗi (kiểm tra container searxng + formats json trong settings.yml).");

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                return ToolResult.Ok("Không có kết quả.");

            var sb = new StringBuilder();
            var index = 0;
            foreach (var item in results.EnumerateArray())
            {
                if (index >= max) break;
                var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
                var link = item.TryGetProperty("url", out var u) ? u.GetString() : null;
                var content = item.TryGetProperty("content", out var c) ? c.GetString() : null;
                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(content)) continue;

                index++;
                if (content is { Length: > MaxSnippetChars })
                    content = content[..MaxSnippetChars] + "…";
                sb.Append(CultureInfo.InvariantCulture, $"{index}. {title}\n");
                if (!string.IsNullOrWhiteSpace(link)) sb.Append(CultureInfo.InvariantCulture, $"   {link}\n");
                if (!string.IsNullOrWhiteSpace(content)) sb.Append(CultureInfo.InvariantCulture, $"   {content}\n");
            }

            return index == 0 ? ToolResult.Ok("Không có kết quả.") : ToolResult.Ok(sb.ToString());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            LogSearchFailed(logger, query, ex.Message);
            return ToolResult.Fail($"web_search_failed: {ex.Message}");
        }
    }

    [LoggerMessage(EventId = 5301, Level = LogLevel.Warning, Message = "web.search failed for '{Query}': {Error}")]
    private static partial void LogSearchFailed(ILogger logger, string query, string error);
}
