using System.Globalization;
using System.Text;
using Clawbot.Api.Contracts.Analytics;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Clawbot.Api.Services;

public sealed class AnalyticsExportService
{
    static AnalyticsExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static string NormalizeFormat(string? format)
    {
        var normalized = string.IsNullOrWhiteSpace(format) ? "csv" : format.Trim().ToLowerInvariant();
        if (normalized is "csv" or "pdf")
            return normalized;

        throw new ArgumentException("Unsupported export format.", nameof(format));
    }

    public static string BuildCsv(IEnumerable<OmniChannelRowDto> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var sb = new StringBuilder();
        sb.Append("platform,leads,dms,replies,conversions,avg_response_time_sec,ad_spend,cpl\r\n");
        foreach (var row in rows)
        {
            sb.Append(Escape(row.Platform)).Append(',')
                .Append(row.Leads.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Dms.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Replies.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Conversions.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(FormatDecimal(row.AvgResponseTimeSec)).Append(',')
                .Append(FormatDecimal(row.AdSpend)).Append(',')
                .Append(FormatDecimal(row.Cpl)).Append("\r\n");
        }

        return sb.ToString();
    }

    public static byte[] BuildPdf(IEnumerable<OmniChannelRowDto> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var snapshot = rows.ToList();
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(32);
                page.DefaultTextStyle(t => t.FontSize(9).FontFamily(Fonts.Calibri));

                page.Header().Text("Clawbot Analytics KPI")
                    .FontSize(16)
                    .SemiBold()
                    .FontColor(Colors.Blue.Darken2);

                page.Content().PaddingTop(12).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1.4f);
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn(1.4f);
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    Header(table, "Platform");
                    Header(table, "Leads");
                    Header(table, "DMs");
                    Header(table, "Replies");
                    Header(table, "Conv.");
                    Header(table, "Resp. sec");
                    Header(table, "Spend");
                    Header(table, "CPL");

                    foreach (var row in snapshot)
                    {
                        Cell(table, row.Platform);
                        Cell(table, row.Leads.ToString(CultureInfo.InvariantCulture));
                        Cell(table, row.Dms.ToString(CultureInfo.InvariantCulture));
                        Cell(table, row.Replies.ToString(CultureInfo.InvariantCulture));
                        Cell(table, row.Conversions.ToString(CultureInfo.InvariantCulture));
                        Cell(table, FormatDecimal(row.AvgResponseTimeSec));
                        Cell(table, FormatDecimal(row.AdSpend));
                        Cell(table, FormatDecimal(row.Cpl));
                    }
                });
            });
        });

        return document.GeneratePdf();
    }

    private static string FormatDecimal(decimal? value) =>
        value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;

    private static string Escape(string value)
    {
        if (!value.Contains('"', StringComparison.Ordinal) &&
            !value.Contains(',', StringComparison.Ordinal) &&
            !value.Contains('\n', StringComparison.Ordinal) &&
            !value.Contains('\r', StringComparison.Ordinal))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static void Header(TableDescriptor table, string text) =>
        table.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(text).SemiBold();

    private static void Cell(TableDescriptor table, string text) =>
        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(text);
}
