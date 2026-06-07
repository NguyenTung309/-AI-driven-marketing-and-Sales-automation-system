using Clawbot.Agents.Contracts.Research;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using CoreResearch = Clawbot.Agents.Core.Research;

namespace Clawbot.AgentService.Services;

public sealed class ResearchAgentGrpcService(
    CoreResearch.IResearchAgent agent,
    AppDbContext db,
    IClock clock) : ResearchAgent.ResearchAgentBase
{
    private const string DefaultGeo = "VN";

    private readonly CoreResearch.IResearchAgent _agent = agent;
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;

    public override async Task<TrendResponse> WeeklyTrends(TrendRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var tenantId = ParseTenantId(request.TenantId);
        var weekOf = ParseWeekOf(request.WeekOf);
        var keywords = await LoadKeywordsAsync(tenantId, context.CancellationToken).ConfigureAwait(false);
        var trends = await _agent.ScanAsync(
            new CoreResearch.ResearchScanRequest(tenantId, DefaultGeo, keywords),
            context.CancellationToken).ConfigureAwait(false);

        await UpsertTrendBriefsAsync(tenantId, weekOf, trends, context.CancellationToken).ConfigureAwait(false);

        var response = new TrendResponse();
        response.Trends.AddRange(trends.Select(ToTrendItem));
        return response;
    }

    private async Task<IReadOnlyList<string>> LoadKeywordsAsync(Guid tenantId, CancellationToken ct)
    {
        var modules = await _db.KbModules.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.DeletedAt == null && m.Status == "active")
            .Select(m => new { m.Code, m.Name, m.Description })
            .ToListAsync(ct).ConfigureAwait(false);

        return modules
            .SelectMany(m => ExtractKeywords(m.Code, m.Name, m.Description))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task UpsertTrendBriefsAsync(
        Guid tenantId,
        string weekOf,
        IReadOnlyList<CoreResearch.ScoredTrend> trends,
        CancellationToken ct)
    {
        if (trends.Count == 0)
            return;

        var weekPrefix = $"[trend:{weekOf}]";
        var existing = await _db.ContentBriefs.IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId && b.Brief.StartsWith(weekPrefix))
            .ToListAsync(ct).ConfigureAwait(false);
        var existingByKey = existing
            .Select(b => new { Brief = b, Parsed = ParseTrendBrief(b.Brief) })
            .Where(x => x.Parsed is not null)
            .GroupBy(x => TrendKey(x.Brief.Platform, x.Parsed!.Topic), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.Brief.UpdatedAt).First().Brief,
                StringComparer.OrdinalIgnoreCase);

        var now = _clock.UtcNow;
        foreach (var trend in trends)
        {
            var brief = new ContentTrendBrief(
                weekOf,
                trend.Topic,
                trend.Source,
                trend.Metric,
                trend.RelevanceScore,
                trend.ContentIdeas);
            var body = ContentTrendBriefFormatter.Format(brief);
            var key = TrendKey(trend.Source, trend.Topic);

            if (existingByKey.TryGetValue(key, out var current))
            {
                current.Update(trend.Source, body, now);
                current.MarkStatus("pending", now);
                continue;
            }

            _db.ContentBriefs.Add(ContentBrief.Create(tenantId, trend.Source, body, createdBy: null, createdAt: now));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private string ParseWeekOf(string weekOf)
    {
        try
        {
            return ContentTrendBriefFormatter.NormalizeWeekOfOrCurrent(weekOf, _clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
    }

    private static Guid ParseTenantId(string tenantId)
    {
        if (!Guid.TryParse(tenantId, out var parsed) || parsed == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "tenant_id required"));

        return parsed;
    }

    private static ContentTrendBrief? ParseTrendBrief(string brief) =>
        ContentTrendBriefFormatter.TryParse(brief, out var parsed) ? parsed : null;

    private static string TrendKey(string source, string topic) => $"{source.Trim().ToLowerInvariant()}::{topic.Trim().ToLowerInvariant()}";

    private static IEnumerable<string> ExtractKeywords(params string?[] fields)
    {
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field))
                continue;

            var trimmed = field.Trim();
            yield return trimmed;

            foreach (var token in trimmed.Split(
                [' ', ',', ';', '.', ':', '/', '\\', '|', '-', '_', '(', ')', '[', ']'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token.Length >= 2)
                    yield return token;
            }
        }
    }

    private static TrendItem ToTrendItem(CoreResearch.ScoredTrend trend)
    {
        var item = new TrendItem
        {
            Topic = trend.Topic,
            Source = trend.Source,
            Metric = trend.Metric,
            RelevanceScore = trend.RelevanceScore,
        };
        item.ContentIdeas.AddRange(trend.ContentIdeas);
        return item;
    }
}
