using Clawbot.Domain.Contacts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Tests;

public sealed class TenantFilterModelCacheTests
{
    [Fact]
    public async Task Tenant_filter_uses_current_context_tenant_after_model_cache_is_built()
    {
        var firstTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var now = new DateTimeOffset(2026, 6, 15, 17, 0, 0, TimeSpan.Zero);

        using (var first = new TestApiAppDb(firstTenant))
        {
            first.Db.Contacts.Add(Contact.Create(firstTenant, "Tenant A", now));
            await first.Db.SaveChangesAsync();
            (await first.Db.Contacts.CountAsync()).Should().Be(1);
        }

        using var second = new TestApiAppDb(secondTenant);
        second.Db.Contacts.Add(Contact.Create(secondTenant, "Tenant B", now));
        await second.Db.SaveChangesAsync();

        var visible = await second.Db.Contacts.Select(c => c.DisplayName).ToListAsync();

        visible.Should().Equal("Tenant B");
    }
}
