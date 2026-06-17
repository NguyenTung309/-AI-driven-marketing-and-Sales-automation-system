using System.Globalization;
using System.Text;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Services;

public sealed class LeadCsvService(AppDbContext db, IClock clock)
{
    private const string FileName = "leads.csv";
    private static readonly string[] RequiredImportColumns = ["display_name", "source_platform"];
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;

    public async Task<LeadCsvExportResult> ExportCsvAsync(Guid tenantId, CancellationToken ct = default)
    {
        var leads = await _db.Leads.IgnoreQueryFilters()
            .Where(l => l.TenantId == tenantId && l.DeletedAt == null)
            .OrderByDescending(l => l.Score)
            .ThenByDescending(l => l.LastActivityAt ?? l.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var contactIds = leads
            .Where(l => l.ContactId.HasValue)
            .Select(l => l.ContactId!.Value)
            .Distinct()
            .ToList();

        var contacts = contactIds.Count == 0
            ? new Dictionary<Guid, Contact>()
            : await _db.Contacts.IgnoreQueryFilters()
                .Where(c => c.TenantId == tenantId && contactIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct)
                .ConfigureAwait(false);

        var csv = new StringBuilder();
        csv.AppendLine("lead_id,contact_id,display_name,phone,email,source_platform,score,stage,owner_user_id,last_activity_at,created_at");

        foreach (var lead in leads)
        {
            contacts.TryGetValue(lead.ContactId ?? Guid.Empty, out var contact);
            csv.Append(Escape(lead.Id.ToString("D", CultureInfo.InvariantCulture))).Append(',')
                .Append(Escape(lead.ContactId?.ToString("D", CultureInfo.InvariantCulture))).Append(',')
                .Append(Escape(contact?.DisplayName)).Append(',')
                .Append(Escape(contact?.Phone)).Append(',')
                .Append(Escape(contact?.Email)).Append(',')
                .Append(Escape(lead.SourcePlatform)).Append(',')
                .Append(Escape(lead.Score.ToString(CultureInfo.InvariantCulture))).Append(',')
                .Append(Escape(lead.Stage)).Append(',')
                .Append(Escape(lead.OwnerUserId?.ToString("D", CultureInfo.InvariantCulture))).Append(',')
                .Append(Escape(lead.LastActivityAt?.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                .Append(Escape(lead.CreatedAt.ToString("O", CultureInfo.InvariantCulture)))
                .AppendLine();
        }

        return new LeadCsvExportResult(FileName, csv.ToString());
    }

    public async Task<LeadCsvImportResult> ImportCsvAsync(Guid tenantId, string csv, CancellationToken ct = default)
    {
        var rows = Parse(csv);
        if (rows.Count == 0)
            return new LeadCsvImportResult(0, [], ["CSV header is required"]);

        var header = rows[0].Values
            .Select((name, index) => new { Name = name.Trim().TrimStart('\uFEFF'), Index = index })
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .ToDictionary(x => x.Name, x => x.Index, StringComparer.OrdinalIgnoreCase);

        var missing = RequiredImportColumns
            .Where(required => !header.ContainsKey(required))
            .ToList();
        if (missing.Count > 0)
            return new LeadCsvImportResult(0, [], [$"missing required column(s): {string.Join(", ", missing)}"]);

        var importedLeadIds = new List<Guid>();
        var errors = new List<string>();
        var now = _clock.UtcNow;

        foreach (var row in rows.Skip(1))
        {
            if (row.Values.All(string.IsNullOrWhiteSpace))
                continue;

            var displayName = Get(row, header, "display_name").Trim();
            var sourcePlatform = Get(row, header, "source_platform").Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                errors.Add($"row {row.RowNumber}: display_name is required");
                continue;
            }

            if (string.IsNullOrWhiteSpace(sourcePlatform))
            {
                errors.Add($"row {row.RowNumber}: source_platform is required");
                continue;
            }

            var score = 0;
            var scoreText = Get(row, header, "score").Trim();
            if (!string.IsNullOrWhiteSpace(scoreText) &&
                (!int.TryParse(scoreText, NumberStyles.Integer, CultureInfo.InvariantCulture, out score) || score is < 0 or > 100))
            {
                errors.Add($"row {row.RowNumber}: score must be an integer between 0 and 100");
                continue;
            }

            var contact = Contact.Create(tenantId, displayName, now);
            SetOptionalContactValue(contact, nameof(Contact.Phone), Get(row, header, "phone"));
            SetOptionalContactValue(contact, nameof(Contact.Email), Get(row, header, "email"));

            var lead = Lead.Create(tenantId, contact.Id, sourcePlatform, now);
            if (score > 0)
                lead.AdjustScore(score, "csv_import", now);

            _db.Contacts.Add(contact);
            _db.Leads.Add(lead);
            importedLeadIds.Add(lead.Id);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new LeadCsvImportResult(importedLeadIds.Count, importedLeadIds, errors);
    }

    private void SetOptionalContactValue(Contact contact, string propertyName, string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        _db.Entry(contact).Property(propertyName).CurrentValue = normalized;
    }

    private static string Get(CsvRow row, Dictionary<string, int> header, string column)
    {
        if (!header.TryGetValue(column, out var index) || index >= row.Values.Count)
            return string.Empty;

        return row.Values[index];
    }

    private static List<CsvRow> Parse(string csv)
    {
        var rows = new List<CsvRow>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var rowNumber = 1;
        var currentRowNumber = 1;

        for (var i = 0; i < csv.Length; i++)
        {
            var ch = csv[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                    if (ch == '\n')
                        rowNumber++;
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    fields.Add(field.ToString());
                    AddRow(rows, currentRowNumber, fields);
                    fields.Clear();
                    field.Clear();
                    if (i + 1 < csv.Length && csv[i + 1] == '\n')
                        i++;
                    rowNumber++;
                    currentRowNumber = rowNumber;
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    AddRow(rows, currentRowNumber, fields);
                    fields.Clear();
                    field.Clear();
                    rowNumber++;
                    currentRowNumber = rowNumber;
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            AddRow(rows, currentRowNumber, fields);
        }

        return rows;
    }

    private static void AddRow(List<CsvRow> rows, int rowNumber, List<string> fields)
    {
        if (fields.Count == 1 && string.IsNullOrWhiteSpace(fields.First()))
            return;

        rows.Add(new CsvRow(rowNumber, fields.ToArray()));
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private sealed record CsvRow(int RowNumber, IReadOnlyList<string> Values);
}

public sealed record LeadCsvExportResult(string FileName, string Content);

public sealed record LeadCsvImportResult(int Imported, IReadOnlyList<Guid> LeadIds, IReadOnlyList<string> Errors);
