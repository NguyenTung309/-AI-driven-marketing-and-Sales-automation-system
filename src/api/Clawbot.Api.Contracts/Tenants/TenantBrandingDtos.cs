namespace Clawbot.Api.Contracts.Tenants;

public sealed record TenantBrandingDto(
    string BrandName,
    string? LogoUrl,
    string PrimaryColor,
    string AccentColor,
    string SupportName,
    string WidgetGreeting);

public sealed record UpdateTenantBrandingRequest(
    string? BrandName,
    string? LogoUrl,
    string? PrimaryColor,
    string? AccentColor,
    string? SupportName,
    string? WidgetGreeting);
