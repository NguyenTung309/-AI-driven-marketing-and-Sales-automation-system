using System.Globalization;
using Clawbot.Agents.Core.Rag;
using Clawbot.Domain.Contacts;
using Clawbot.SharedKernel.Vectors;

namespace Clawbot.Infrastructure.Vectors;

public interface IContactEmbeddingSync
{
    Task UpsertContactAsync(Contact contact, Guid tenantId, CancellationToken ct = default);
    Task BackfillAllAsync(Guid tenantId, IReadOnlyList<Contact> contacts, CancellationToken ct = default);
}

// Upserts contact embedding to Qdrant "contacts" collection on create/ingest
// so ILeadDeduplicator (fuzzy) has vectors to search against.
public sealed class ContactEmbeddingSync : IContactEmbeddingSync
{
    private const string Collection = "contacts";
    private readonly IEmbeddingProvider _embedding;
    private readonly IVectorStore _store;

    public ContactEmbeddingSync(IEmbeddingProvider embedding, IVectorStore store)
    {
        _embedding = embedding;
        _store = store;
    }

    public async Task UpsertContactAsync(Contact contact, Guid tenantId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contact);

        var key = BuildKeyString(contact);
        if (string.IsNullOrWhiteSpace(key)) return;

        var embedding = await _embedding.EmbedAsync(key, ct).ConfigureAwait(false);
        var record = new VectorRecord(
            Id: contact.Id.ToString("D"),
            Embedding: embedding,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tenant_id"] = tenantId.ToString("D", CultureInfo.InvariantCulture),
                ["contact_id"] = contact.Id.ToString("D", CultureInfo.InvariantCulture),
                ["display_name"] = contact.DisplayName ?? "",
                ["phone_tail"] = GetPhoneTail(contact.Phone),
                ["email"] = contact.Email ?? ""
            });

        await _store.UpsertAsync(Collection, new[] { record }, ct).ConfigureAwait(false);
    }

    public async Task BackfillAllAsync(Guid tenantId, IReadOnlyList<Contact> contacts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        if (contacts.Count == 0) return;

        var records = new List<VectorRecord>(contacts.Count);
        foreach (var contact in contacts)
        {
            ct.ThrowIfCancellationRequested();
            var key = BuildKeyString(contact);
            if (string.IsNullOrWhiteSpace(key)) continue;

            var embedding = await _embedding.EmbedAsync(key, ct).ConfigureAwait(false);
            records.Add(new VectorRecord(
                Id: contact.Id.ToString("D"),
                Embedding: embedding,
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tenant_id"] = tenantId.ToString("D", CultureInfo.InvariantCulture),
                    ["contact_id"] = contact.Id.ToString("D", CultureInfo.InvariantCulture),
                    ["display_name"] = contact.DisplayName ?? "",
                    ["phone_tail"] = GetPhoneTail(contact.Phone),
                    ["email"] = contact.Email ?? ""
                }));
        }

        if (records.Count > 0)
            await _store.UpsertAsync(Collection, records, ct).ConfigureAwait(false);
    }

    private static string BuildKeyString(Contact c)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(c.DisplayName)) parts.Add(c.DisplayName);
        if (!string.IsNullOrWhiteSpace(c.Phone)) parts.Add(GetPhoneTail(c.Phone));
        if (!string.IsNullOrWhiteSpace(c.Email)) parts.Add(c.Email);
        return string.Join(" | ", parts);
    }

    private static string GetPhoneTail(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "";
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length >= 7 ? digits[^7..] : digits;
    }
}
