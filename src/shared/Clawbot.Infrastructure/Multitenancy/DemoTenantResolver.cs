using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Multitenancy;

public sealed class DemoTenantResolver : ITenantResolver
{
    private readonly AppDbContext _db;

    public DemoTenantResolver(AppDbContext db) => _db = db;

    public async Task<Guid> ResolveTenantIdAsync(CancellationToken ct = default)
    {
        var tenant = await _db.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(t => t.Slug == "demo" || t.Slug == "default")
            .FirstOrDefaultAsync(ct);

        if (tenant is not null) return tenant.Id;

        tenant = await _db.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct);

        if (tenant is not null) return tenant.Id;

        return Guid.Parse("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");
    }
}
