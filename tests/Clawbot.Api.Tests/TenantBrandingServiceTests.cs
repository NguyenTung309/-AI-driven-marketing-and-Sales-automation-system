using Clawbot.Api.Contracts.Tenants;
using Clawbot.Api.Services;
using Clawbot.Domain.Tenants;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Tests;

public sealed class TenantBrandingServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("c8c8c8c8-c8c8-c8c8-c8c8-c8c8c8c8c8c8");
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_returns_display_name_based_defaults_when_branding_is_empty()
    {
        using var fx = new TestApiAppDb(TenantId);
        var tenant = Tenant.Create("hoc-ba", "Hoc Ba Education", "free", Now);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();
        var sut = new TenantBrandingService(fx.Db);

        var branding = await sut.GetAsync(tenant.Id, CancellationToken.None);

        branding.BrandName.Should().Be("Hoc Ba Education");
        branding.SupportName.Should().Be("Hoc Ba Education Support");
        branding.PrimaryColor.Should().Be("#b91c1c");
        branding.AccentColor.Should().Be("#f59e0b");
        branding.LogoUrl.Should().BeNull();
        branding.WidgetGreeting.Should().Contain("Hoc Ba Education");
    }

    [Fact]
    public async Task UpdateAsync_persists_white_label_fields_and_public_shape()
    {
        using var fx = new TestApiAppDb(TenantId);
        var tenant = Tenant.Create("academy", "Old Academy", "pro", Now);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();
        var sut = new TenantBrandingService(fx.Db);

        var updated = await sut.UpdateAsync(tenant.Id, new UpdateTenantBrandingRequest(
            BrandName: "Lotus Chinese",
            LogoUrl: "https://cdn.example.com/lotus.svg",
            PrimaryColor: "#0f766e",
            AccentColor: "#f97316",
            SupportName: "Lotus Advisor",
            WidgetGreeting: "Lotus xin chao ban."), CancellationToken.None);

        updated.BrandName.Should().Be("Lotus Chinese");
        updated.LogoUrl.Should().Be("https://cdn.example.com/lotus.svg");
        updated.PrimaryColor.Should().Be("#0f766e");
        updated.AccentColor.Should().Be("#f97316");
        updated.SupportName.Should().Be("Lotus Advisor");
        updated.WidgetGreeting.Should().Be("Lotus xin chao ban.");

        var persisted = await fx.Db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenant.Id);
        persisted.BrandName.Should().Be("Lotus Chinese");
        TenantBrandingService.ToPublicBranding(persisted).PrimaryColor.Should().Be("#0f766e");
    }

    [Fact]
    public async Task UpdateAsync_rejects_non_hex_brand_colors()
    {
        using var fx = new TestApiAppDb(TenantId);
        var tenant = Tenant.Create("academy", "Old Academy", "pro", Now);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();
        var sut = new TenantBrandingService(fx.Db);

        var act = () => sut.UpdateAsync(tenant.Id, new UpdateTenantBrandingRequest(
            BrandName: null,
            LogoUrl: null,
            PrimaryColor: "red",
            AccentColor: "#ff00aa",
            SupportName: null,
            WidgetGreeting: null), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*primaryColor*");
    }
}
