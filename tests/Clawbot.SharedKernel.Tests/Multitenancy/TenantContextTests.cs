using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Multitenancy;

public sealed class TenantContextTests
{
    [Fact]
    public void Constructor_SetsFields()
    {
        var tenantId = Guid.NewGuid();
        var ctx = new TenantContext(tenantId, "hocba");

        ctx.TenantId.Should().Be(tenantId);
        ctx.TenantSlug.Should().Be("hocba");
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var id = Guid.NewGuid();
        var a = new TenantContext(id, "slug");
        var b = new TenantContext(id, "slug");

        a.Should().Be(b);
    }
}
