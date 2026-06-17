using System.Security.Cryptography;
using System.Text;
using Clawbot.Domain.Experiments;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Services;

public sealed class ExperimentService(AppDbContext db, IClock clock)
{
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;

    public async Task<ExperimentAssignmentResult> AssignAsync(
        Guid tenantId,
        Guid experimentId,
        string subjectKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subjectKey))
            throw new ArgumentException("subjectKey required", nameof(subjectKey));

        var normalizedSubject = subjectKey.Trim();
        var existing = await _db.ExperimentAssignments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a =>
                a.TenantId == tenantId &&
                a.ExperimentId == experimentId &&
                a.SubjectKey == normalizedSubject, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            var assignedVariant = await _db.ExperimentVariants
                .IgnoreQueryFilters()
                .FirstAsync(v => v.Id == existing.VariantId && v.TenantId == tenantId, ct)
                .ConfigureAwait(false);
            return MapAssignment(experimentId, assignedVariant);
        }

        var experiment = await _db.Experiments
            .IgnoreQueryFilters()
            .Include(e => e.Variants)
            .FirstOrDefaultAsync(e =>
                e.Id == experimentId &&
                e.TenantId == tenantId &&
                e.Status == "active" &&
                e.DeletedAt == null, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("experiment_not_found");

        var variant = PickVariant(experiment, normalizedSubject);
        _db.ExperimentAssignments.Add(ExperimentAssignment.Create(
            tenantId, experiment.Id, variant.Id, normalizedSubject, _clock.UtcNow));
        _db.ExperimentEvents.Add(ExperimentEvent.Create(
            tenantId, experiment.Id, variant.Id, normalizedSubject, "exposure", value: null, _clock.UtcNow));

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return MapAssignment(experiment.Id, variant);
    }

    public async Task RecordEventAsync(
        Guid tenantId,
        Guid experimentId,
        Guid variantId,
        string subjectKey,
        string eventType,
        decimal? value,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(subjectKey))
            throw new ArgumentException("subjectKey required", nameof(subjectKey));
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("eventType required", nameof(eventType));

        var normalizedType = eventType.Trim().ToLowerInvariant();
        var normalizedSubject = subjectKey.Trim();

        var variantExists = await _db.ExperimentVariants
            .IgnoreQueryFilters()
            .AnyAsync(v => v.Id == variantId && v.ExperimentId == experimentId && v.TenantId == tenantId, ct)
            .ConfigureAwait(false);
        if (!variantExists)
            throw new InvalidOperationException("experiment_variant_not_found");

        if (normalizedType is "exposure" or "conversion")
        {
            var duplicate = await _db.ExperimentEvents
                .IgnoreQueryFilters()
                .AnyAsync(e =>
                    e.TenantId == tenantId &&
                    e.ExperimentId == experimentId &&
                    e.VariantId == variantId &&
                    e.SubjectKey == normalizedSubject &&
                    e.EventType == normalizedType, ct)
                .ConfigureAwait(false);
            if (duplicate) return;
        }

        _db.ExperimentEvents.Add(ExperimentEvent.Create(
            tenantId, experimentId, variantId, normalizedSubject, normalizedType, value, _clock.UtcNow));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<ExperimentSummary> GetSummaryAsync(
        Guid tenantId,
        Guid experimentId,
        CancellationToken ct = default)
    {
        var experiment = await _db.Experiments
            .IgnoreQueryFilters()
            .Include(e => e.Variants)
            .FirstOrDefaultAsync(e => e.Id == experimentId && e.TenantId == tenantId, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("experiment_not_found");

        var events = await _db.ExperimentEvents
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && e.ExperimentId == experimentId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var rows = experiment.Variants
            .OrderBy(v => v.Code, StringComparer.Ordinal)
            .Select(v =>
            {
                var exposures = events
                    .Where(e => e.VariantId == v.Id && e.EventType == "exposure")
                    .Select(e => e.SubjectKey)
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                var conversions = events
                    .Where(e => e.VariantId == v.Id && e.EventType == "conversion")
                    .Select(e => e.SubjectKey)
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                var rate = exposures == 0 ? 0m : Math.Round((decimal)conversions / exposures, 4);
                return new ExperimentVariantSummary(v.Id, v.Code, v.Name, v.Weight, exposures, conversions, rate);
            })
            .ToList();

        var winner = rows
            .Where(r => r.Exposures > 0)
            .OrderByDescending(r => r.ConversionRate)
            .ThenByDescending(r => r.Exposures)
            .ThenBy(r => r.Code, StringComparer.Ordinal)
            .FirstOrDefault();

        return new ExperimentSummary(
            experiment.Id,
            experiment.Code,
            experiment.TargetType,
            experiment.TargetId,
            experiment.Status,
            winner?.Code,
            rows);
    }

    private static ExperimentVariant PickVariant(Experiment experiment, string subjectKey)
    {
        var variants = experiment.Variants
            .Where(v => v.Weight > 0)
            .OrderBy(v => v.Code, StringComparer.Ordinal)
            .ToList();
        if (variants.Count == 0)
            throw new InvalidOperationException("experiment_has_no_variants");

        var totalWeight = variants.Sum(v => v.Weight);
        var bucket = StableBucket(experiment.Id, subjectKey, totalWeight);
        var cumulative = 0;
        foreach (var variant in variants)
        {
            cumulative += variant.Weight;
            if (bucket < cumulative)
                return variant;
        }

        return variants[^1];
    }

    private static int StableBucket(Guid experimentId, string subjectKey, int totalWeight)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{experimentId:N}:{subjectKey}"));
        var value = BitConverter.ToUInt32(bytes, 0);
        return (int)(value % (uint)totalWeight);
    }

    private static ExperimentAssignmentResult MapAssignment(Guid experimentId, ExperimentVariant variant) =>
        new(experimentId, variant.Id, variant.Code, variant.ChatScenarioId, variant.KbVersionId);
}

public sealed record ExperimentAssignmentResult(
    Guid ExperimentId,
    Guid VariantId,
    string VariantCode,
    Guid? ChatScenarioId,
    Guid? KbVersionId);

public sealed record ExperimentSummary(
    Guid ExperimentId,
    string Code,
    string TargetType,
    Guid TargetId,
    string Status,
    string? WinnerVariantCode,
    IReadOnlyList<ExperimentVariantSummary> Variants);

public sealed record ExperimentVariantSummary(
    Guid VariantId,
    string Code,
    string Name,
    int Weight,
    int Exposures,
    int Conversions,
    decimal ConversionRate);
