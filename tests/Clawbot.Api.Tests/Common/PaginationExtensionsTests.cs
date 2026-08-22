using Clawbot.Api.Common.Pagination;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Tests.Common;

/// <summary>
/// Hai overload ToPagedResultAsync (entity+selector, và IQueryable&lt;TDto&gt; đã Select sẵn).
/// Seed bằng bảng Tenants (không phải ITenantOwned nên không đụng query filter) qua InMemory
/// AppDbContext để giữ test nhẹ, không cần WebApplicationFactory.
/// </summary>
public sealed class PaginationExtensionsTests : IAsyncDisposable
{
    private readonly AppDbContext _db;

    public PaginationExtensionsTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"pagination-{Guid.NewGuid():N}")
            .Options;
        _db = new AppDbContext(options, new StubTenantAccessor());
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    private async Task SeedTenantsAsync(int count)
    {
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < count; i++)
        {
            _db.Tenants.Add(Tenant.Create($"tenant-{i:D3}", $"Tenant {i:D3}", "free", now));
        }
        await _db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------
    // Overload TEntity -> TDto (selector)
    // ------------------------------------------------------------------

    [Fact]
    public async Task ToPagedResultAsync_WithSelector_ReturnsRequestedPage()
    {
        await SeedTenantsAsync(5);
        var query = _db.Tenants.OrderBy(t => t.Slug);

        var page = await query.ToPagedResultAsync(page: 2, pageSize: 2, selector: t => t.Slug);

        page.Total.Should().Be(5);
        page.Page.Should().Be(2);
        page.PageSize.Should().Be(2);
        page.Items.Should().Equal("tenant-002", "tenant-003");
    }

    [Fact]
    public async Task ToPagedResultAsync_WithSelector_NonPositivePageAndPageSize_ClampsToDefaults()
    {
        await SeedTenantsAsync(3);
        var query = _db.Tenants.OrderBy(t => t.Slug);

        var page = await query.ToPagedResultAsync(page: 0, pageSize: -1, selector: t => t.Slug);

        page.Page.Should().Be(1);
        page.PageSize.Should().Be(PageRequest.DefaultPageSize);
        page.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task ToPagedResultAsync_WithSelector_PageSizeAboveMax_ClampsToDefaultPageSize()
    {
        await SeedTenantsAsync(2);
        var query = _db.Tenants.OrderBy(t => t.Slug);

        var page = await query.ToPagedResultAsync(
            page: 1,
            pageSize: PageRequest.DefaultMaxPageSize + 1,
            selector: t => t.Slug);

        page.PageSize.Should().Be(PageRequest.DefaultPageSize);
    }

    [Fact]
    public async Task ToPagedResultAsync_WithSelector_PageBeyondLastPage_ReturnsEmptyItemsWithCorrectTotal()
    {
        await SeedTenantsAsync(2);
        var query = _db.Tenants.OrderBy(t => t.Slug);

        var page = await query.ToPagedResultAsync(page: 5, pageSize: 10, selector: t => t.Slug);

        page.Total.Should().Be(2);
        page.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ToPagedResultAsync_WithSelector_CustomDefaultAndMaxPageSize_AreRespected()
    {
        await SeedTenantsAsync(4);
        var query = _db.Tenants.OrderBy(t => t.Slug);

        var page = await query.ToPagedResultAsync(
            page: 1,
            pageSize: 0,
            selector: t => t.Slug,
            defaultPageSize: 3,
            maxPageSize: 10);

        page.PageSize.Should().Be(3);
        page.Items.Should().HaveCount(3);
    }

    // ------------------------------------------------------------------
    // Overload IQueryable<TDto> (projection đã Select sẵn)
    // ------------------------------------------------------------------

    [Fact]
    public async Task ToPagedResultAsync_ProjectedQueryable_ReturnsRequestedPage()
    {
        await SeedTenantsAsync(5);
        var query = _db.Tenants.OrderBy(t => t.Slug).Select(t => t.Slug);

        var page = await query.ToPagedResultAsync(page: 1, pageSize: 3);

        page.Total.Should().Be(5);
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(3);
        page.Items.Should().Equal("tenant-000", "tenant-001", "tenant-002");
    }

    [Fact]
    public async Task ToPagedResultAsync_ProjectedQueryable_NegativePage_ClampsToFirstPage()
    {
        await SeedTenantsAsync(3);
        var query = _db.Tenants.OrderBy(t => t.Slug).Select(t => t.Slug);

        var page = await query.ToPagedResultAsync(page: -3, pageSize: 2);

        page.Page.Should().Be(1);
        page.Items.Should().Equal("tenant-000", "tenant-001");
    }

    [Fact]
    public async Task ToPagedResultAsync_ProjectedQueryable_PageSizeAboveMax_ClampsToDefaultPageSize()
    {
        await SeedTenantsAsync(1);
        var query = _db.Tenants.OrderBy(t => t.Slug).Select(t => t.Slug);

        var page = await query.ToPagedResultAsync(page: 1, pageSize: PageRequest.DefaultMaxPageSize * 2);

        page.PageSize.Should().Be(PageRequest.DefaultPageSize);
    }

    [Fact]
    public async Task ToPagedResultAsync_ProjectedQueryable_EmptySource_ReturnsZeroTotalAndEmptyItems()
    {
        var query = _db.Tenants.OrderBy(t => t.Slug).Select(t => t.Slug);

        var page = await query.ToPagedResultAsync(page: 1, pageSize: 10);

        page.Total.Should().Be(0);
        page.Items.Should().BeEmpty();
    }

    private sealed class StubTenantAccessor : ITenantAccessor
    {
        public TenantContext? Current { get; } = new(Guid.NewGuid(), "test-tenant");

        public TenantContext Require() => Current!;
    }
}
