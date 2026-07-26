using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Channels.Meta;

public interface IMetaInboxProvisioner
{
    Task EnsureAsync(
        Guid tenantId,
        string platform,
        string externalPageId,
        string name,
        CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed class MetaInboxProvisioner(
    AppDbContext db,
    IClock clock) : IMetaInboxProvisioner
{
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;

    public async Task EnsureAsync(
        Guid tenantId,
        string platform,
        string externalPageId,
        string name,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenantId required", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(platform))
            throw new ArgumentException("platform required", nameof(platform));
        if (string.IsNullOrWhiteSpace(externalPageId))
            throw new ArgumentException("externalPageId required", nameof(externalPageId));

        var normalizedPlatform = platform.Trim().ToLowerInvariant();
        var normalizedExternalPageId = externalPageId.Trim();
        var tracked = _db.ChangeTracker.Entries<Inbox>().Any(entry =>
            entry.State == EntityState.Added
            && entry.Entity.TenantId == tenantId
            && entry.Entity.Platform == normalizedPlatform
            && entry.Entity.ExternalPageId == normalizedExternalPageId
            && entry.Entity.DeletedAt == null);
        if (tracked)
            return;

        var exists = await _db.Inboxes
            .IgnoreQueryFilters()
            .AnyAsync(inbox => inbox.TenantId == tenantId
                && inbox.Platform == normalizedPlatform
                && inbox.ExternalPageId == normalizedExternalPageId
                && inbox.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (exists)
            return;

        var displayName = string.IsNullOrWhiteSpace(name)
            ? $"Meta {normalizedPlatform} - {normalizedExternalPageId}"
            : name.Trim();
        var inbox = Inbox.Create(tenantId, displayName, normalizedPlatform, normalizedExternalPageId);
        inbox.SetSenderId(normalizedExternalPageId);
        // Inbox.Create uses UTC for its initial timestamps; touch the row with the shared clock so
        // replayed webhook/job provisioning remains consistent with the rest of the infrastructure.
        inbox.UpdateName(displayName, _clock.UtcNow);
        _db.Inboxes.Add(inbox);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsInboxIdentityConflict(exception))
        {
            var addedEntries = _db.ChangeTracker.Entries<Inbox>()
                .Where(entry => entry.State == EntityState.Added)
                .ToList();
            foreach (var entry in addedEntries)
            {
                var exists = await _db.Inboxes
                    .IgnoreQueryFilters()
                    .AnyAsync(inbox => inbox.TenantId == entry.Entity.TenantId
                        && inbox.Platform == entry.Entity.Platform
                        && inbox.ExternalPageId == entry.Entity.ExternalPageId
                        && inbox.IsActive
                        && inbox.DeletedAt == null, ct)
                    .ConfigureAwait(false);
                if (exists)
                    entry.State = EntityState.Detached;
            }
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    private static bool IsInboxIdentityConflict(DbUpdateException exception) =>
        exception.InnerException is SqlException sql
        && (sql.Number == 2601 || sql.Number == 2627)
        && sql.Message.Contains("UX_inboxes_tenant_platform_external_active", StringComparison.OrdinalIgnoreCase);
}
