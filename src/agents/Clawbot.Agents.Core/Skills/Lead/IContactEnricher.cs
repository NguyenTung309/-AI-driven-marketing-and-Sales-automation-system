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

// Source: https://hunter.io/api  +  https://apollo.io  (fallback chain).
// Cost-controlled: cache hit in Redis 24h, only call provider when miss.
internal sealed class HunterContactEnricher : IContactEnricher
{
    public string Name => "contact-enrichment";

    public Task<ContactEnrichment?> EnrichByEmailAsync(string email, CancellationToken ct) =>
        throw new NotImplementedException("TODO: Hunter.io GET /v2/people/find?email=… via HttpClient.");

    public Task<ContactEnrichment?> EnrichByPhoneAsync(string phone, CancellationToken ct) =>
        throw new NotImplementedException("TODO: Apollo.io /people/match via HttpClient.");
}
