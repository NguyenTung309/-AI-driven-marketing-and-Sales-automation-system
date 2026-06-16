using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawbot.Agents.Core.Skills.Lead;

public sealed record ContactEnrichment(
    string? FullName,
    string? Company,
    string? JobTitle,
    string? LinkedIn,
    string? Country,
    IReadOnlyDictionary<string, string> Extra);

public interface IContactEnricher : ISkill
{
    Task<ContactEnrichment?> EnrichByEmailAsync(string email, CancellationToken ct);
    Task<ContactEnrichment?> EnrichByPhoneAsync(string phone, CancellationToken ct);
}

public sealed class ContactEnricherOptions
{
    public const string SectionName = "Skills:ContactEnrich";
    public bool Enabled { get; set; }
    public string HunterApiKey { get; set; } = string.Empty;
    public string HunterBaseUrl { get; set; } = "https://api.hunter.io/v2";
    public string ApolloApiKey { get; set; } = string.Empty;
    public string ApolloBaseUrl { get; set; } = "https://api.apollo.io/v1";
    public int TimeoutSeconds { get; set; } = 15;
}

// Config-gated Hunter (email) + Apollo (phone) enrichment.
// Graceful null when disabled/no-key. Minimal heuristic (email domain → company) when external off.
internal sealed partial class HunterContactEnricher : IContactEnricher
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ContactEnricherOptions _options;
    private readonly ILogger<HunterContactEnricher> _logger;

    public HunterContactEnricher(
        IHttpClientFactory httpFactory,
        IOptions<ContactEnricherOptions> options,
        ILogger<HunterContactEnricher> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "contact-enrichment";

    public async Task<ContactEnrichment?> EnrichByEmailAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        // Try Hunter.io if configured
        if (_options.Enabled && !string.IsNullOrWhiteSpace(_options.HunterApiKey))
        {
            try
            {
                return await HunterLookupAsync(email, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogHunterFailed(_logger, ex, email);
            }
        }

        // Heuristic: email domain → company
        return HeuristicFromEmail(email);
    }

    public async Task<ContactEnrichment?> EnrichByPhoneAsync(string phone, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        // Try Apollo if configured
        if (_options.Enabled && !string.IsNullOrWhiteSpace(_options.ApolloApiKey))
        {
            try
            {
                return await ApolloLookupAsync(phone, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogApolloFailed(_logger, ex, phone);
            }
        }

        return null;
    }

    private async Task<ContactEnrichment?> HunterLookupAsync(string email, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient(nameof(HunterContactEnricher));
        var url = $"{_options.HunterBaseUrl}/people/find?email={Uri.EscapeDataString(email)}&api_key={_options.HunterApiKey}";
        var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            LogHunterStatus(_logger, (int)resp.StatusCode, email);
            return null;
        }

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct).ConfigureAwait(false);
        if (!json.TryGetProperty("data", out var data)) return null;

        return new ContactEnrichment(
            FullName: data.TryGetProperty("full_name", out var fn) ? fn.GetString() : null,
            Company: data.TryGetProperty("company", out var co) ? co.GetString() : null,
            JobTitle: data.TryGetProperty("position", out var pos) ? pos.GetString() : null,
            LinkedIn: data.TryGetProperty("linkedin", out var li) ? li.GetString() : null,
            Country: data.TryGetProperty("country", out var cn) ? cn.GetString() : null,
            Extra: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private async Task<ContactEnrichment?> ApolloLookupAsync(string phone, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient(nameof(HunterContactEnricher));
        var payload = new { api_key = _options.ApolloApiKey, phone_numbers = new[] { phone } };
        var resp = await http.PostAsJsonAsync($"{_options.ApolloBaseUrl}/people/match", payload, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            LogApolloStatus(_logger, (int)resp.StatusCode, phone);
            return null;
        }

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct).ConfigureAwait(false);
        if (!json.TryGetProperty("person", out var person)) return null;

        return new ContactEnrichment(
            FullName: person.TryGetProperty("name", out var nm) ? nm.GetString() : null,
            Company: person.TryGetProperty("organization", out var org) && org.TryGetProperty("name", out var orgName) ? orgName.GetString() : null,
            JobTitle: person.TryGetProperty("title", out var ti) ? ti.GetString() : null,
            LinkedIn: person.TryGetProperty("linkedin_url", out var li) ? li.GetString() : null,
            Country: person.TryGetProperty("country", out var cn) ? cn.GetString() : null,
            Extra: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static ContactEnrichment? HeuristicFromEmail(string email)
    {
        var atIdx = email.LastIndexOf('@');
        if (atIdx <= 0 || atIdx >= email.Length - 1) return null;

        var domain = email[(atIdx + 1)..];
        if (domain.EndsWith(".edu", StringComparison.OrdinalIgnoreCase) ||
            domain.EndsWith(".edu.vn", StringComparison.OrdinalIgnoreCase))
        {
            var name = domain.Split('.')[0];
            return new ContactEnrichment(null, Capitalize(name), null, null, null,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        // Skip generic email providers
        var genericDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gmail.com", "yahoo.com", "hotmail.com", "outlook.com", "icloud.com",
            "protonmail.com", "mail.com", "yandex.com", "qq.com", "163.com", "126.com"
        };
        if (genericDomains.Contains(domain)) return null;

        var company = domain.Split('.')[0];
        return new ContactEnrichment(null, Capitalize(company), null, null, null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static string Capitalize(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    [LoggerMessage(EventId = 6001, Level = LogLevel.Warning, Message = "Hunter.io lookup failed for {Email}")]
    private static partial void LogHunterFailed(ILogger logger, Exception ex, string email);

    [LoggerMessage(EventId = 6002, Level = LogLevel.Warning, Message = "Apollo lookup failed for {Phone}")]
    private static partial void LogApolloFailed(ILogger logger, Exception ex, string phone);

    [LoggerMessage(EventId = 6003, Level = LogLevel.Warning, Message = "Hunter.io returned {Status} for {Email}")]
    private static partial void LogHunterStatus(ILogger logger, int status, string email);

    [LoggerMessage(EventId = 6004, Level = LogLevel.Warning, Message = "Apollo returned {Status} for {Phone}")]
    private static partial void LogApolloStatus(ILogger logger, int status, string phone);
}
