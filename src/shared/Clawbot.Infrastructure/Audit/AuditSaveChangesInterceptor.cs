using System.Globalization;
using System.Text.Json;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Common;
using Clawbot.Domain.Security;
using Clawbot.SharedKernel.Audit;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Clawbot.Infrastructure.Audit;

public sealed class AuditSaveChangesInterceptor(
    IAuditContext audit,
    ITenantAccessor tenants,
    IPiiRedactor pii,
    IClock clock) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private static readonly string[] SensitiveProps =
        { "PasswordHash", "SecurityStamp", "AccessToken", "RefreshToken", "ApiKey", "Secret", "Token" };

    private readonly IAuditContext _audit = audit;
    private readonly ITenantAccessor _tenants = tenants;
    private readonly IPiiRedactor _pii = pii;
    private readonly IClock _clock = clock;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null) return result;
        await EmitAuditLogsAsync(eventData.Context, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task EmitAuditLogsAsync(DbContext ctx, CancellationToken ct)
    {
        var tenant = _tenants.Current;
        if (tenant is null) return;

        var entries = ctx.ChangeTracker.Entries()
            .Where(e => e.Entity is not AuditLog
                && (e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted))
            .ToList();

        if (entries.Count == 0) return;

        var now = _clock.UtcNow;
        foreach (var entry in entries)
        {
            var resourceType = entry.Metadata.ClrType.Name;
            var resourceId = TryExtractId(entry);
            var action = entry.State switch
            {
                EntityState.Added => "create",
                EntityState.Modified => "update",
                EntityState.Deleted => "delete",
                _ => "unknown",
            };

            var diffJson = await BuildDiffJsonAsync(entry, ct).ConfigureAwait(false);

            ctx.Add(AuditLog.Create(
                tenantId: ResolveTenantId(entry, tenant.TenantId),
                userId: _audit.UserId,
                action: action,
                resourceType: resourceType,
                resourceId: resourceId,
                occurredAt: now,
                diffJson: diffJson,
                ip: _audit.IpAddress,
                userAgent: _audit.UserAgent));
        }
    }

    private static Guid ResolveTenantId(EntityEntry entry, Guid fallback)
    {
        if (entry.Entity is ITenantOwned owned && owned.TenantId != Guid.Empty)
            return owned.TenantId;
        return fallback;
    }

    private static Guid? TryExtractId(EntityEntry entry)
    {
        var prop = entry.Properties.FirstOrDefault(p => string.Equals(p.Metadata.Name, "Id", StringComparison.Ordinal));
        if (prop?.CurrentValue is Guid g) return g;
        return null;
    }

    private async Task<string?> BuildDiffJsonAsync(EntityEntry entry, CancellationToken ct)
    {
        var diff = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in entry.Properties)
        {
            var name = prop.Metadata.Name;
            if (IsSensitive(name)) continue;

            var current = prop.CurrentValue;
            var original = prop.OriginalValue;

            switch (entry.State)
            {
                case EntityState.Added:
                    diff[name] = await RedactValueAsync(current, ct).ConfigureAwait(false);
                    break;
                case EntityState.Deleted:
                    diff[name] = new { from = await RedactValueAsync(original, ct).ConfigureAwait(false), to = (object?)null };
                    break;
                case EntityState.Modified:
                    if (!Equals(current, original))
                    {
                        diff[name] = new
                        {
                            from = await RedactValueAsync(original, ct).ConfigureAwait(false),
                            to = await RedactValueAsync(current, ct).ConfigureAwait(false),
                        };
                    }
                    break;
            }
        }

        return diff.Count == 0 ? null : JsonSerializer.Serialize(diff, JsonOpts);
    }

    private async Task<object?> RedactValueAsync(object? value, CancellationToken ct)
    {
        if (value is null) return null;
        if (value is string s && s.Length > 0)
        {
            var r = await _pii.RedactAsync(s, ct).ConfigureAwait(false);
            return r.RedactedText;
        }
        if (value is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
        return value.ToString();
    }

    private static bool IsSensitive(string name)
    {
        foreach (var s in SensitiveProps)
        {
            if (name.Contains(s, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
