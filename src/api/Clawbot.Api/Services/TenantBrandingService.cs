using System.Text.RegularExpressions;
using Clawbot.Api.Contracts.Tenants;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Services;

public sealed partial class TenantBrandingService(AppDbContext db)
{
    public const string DefaultPrimaryColor = "#b91c1c";
    public const string DefaultAccentColor = "#f59e0b";

    private readonly AppDbContext _db = db;

    public async Task<TenantBrandingDto> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId && t.IsActive, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("tenant_not_found");

        return ToPublicBranding(tenant);
    }

    public async Task<TenantBrandingDto> UpdateAsync(
        Guid tenantId,
        UpdateTenantBrandingRequest request,
        CancellationToken ct = default)
    {
        var primaryColor = NormalizeColor(request.PrimaryColor, nameof(request.PrimaryColor));
        var accentColor = NormalizeColor(request.AccentColor, nameof(request.AccentColor));

        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tenantId && t.IsActive, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("tenant_not_found");

        tenant.UpdateBranding(
            request.BrandName,
            request.LogoUrl,
            primaryColor,
            accentColor,
            request.SupportName,
            request.WidgetGreeting);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToPublicBranding(tenant);
    }

    public static TenantBrandingDto ToPublicBranding(Tenant tenant)
    {
        var brandName = string.IsNullOrWhiteSpace(tenant.BrandName)
            ? tenant.DisplayName
            : tenant.BrandName.Trim();
        var supportName = string.IsNullOrWhiteSpace(tenant.SupportName)
            ? $"{brandName} Support"
            : tenant.SupportName.Trim();
        var greeting = string.IsNullOrWhiteSpace(tenant.WidgetGreeting)
            ? $"Chao ban, {brandName} co the ho tro tu van lo trinh hoc va lich kiem tra dau vao."
            : tenant.WidgetGreeting.Trim();

        return new TenantBrandingDto(
            brandName,
            string.IsNullOrWhiteSpace(tenant.LogoUrl) ? null : tenant.LogoUrl.Trim(),
            string.IsNullOrWhiteSpace(tenant.PrimaryColor) ? DefaultPrimaryColor : tenant.PrimaryColor.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(tenant.AccentColor) ? DefaultAccentColor : tenant.AccentColor.Trim().ToLowerInvariant(),
            supportName,
            greeting);
    }

    private static string? NormalizeColor(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim().ToLowerInvariant();
        if (!HexColorRegex().IsMatch(trimmed))
            throw new ArgumentException($"{fieldName} must be a #RRGGBB hex color.", fieldName);
        return trimmed;
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorRegex();
}
