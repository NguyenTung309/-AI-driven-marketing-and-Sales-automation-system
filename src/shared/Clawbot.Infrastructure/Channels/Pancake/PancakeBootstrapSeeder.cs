using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Channels.Pancake;

// SPEC-16 Module M-1: startup bootstrap that moves a Pancake page token from environment
// configuration into the encrypted canonical inbox credential store.
public static partial class PancakeBootstrapSeeder
{
    public static async Task BootstrapAsync(
        IServiceProvider services,
        IConfiguration cfg,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(cfg);

        var pageToken = NormalizeCredential(cfg["PANCAKE_PAGE_ACCESS_TOKEN"]);
        var userToken = NormalizeCredential(cfg["PANCAKE_USER_ACCESS_TOKEN"]);
        if (pageToken is null && userToken is null)
            return;

        var pageId = NormalizeRequired(
            cfg["PANCAKE_PAGE_ID"],
            "pancake_bootstrap_page_id_required",
            128);
        var tenantSlug = NormalizeRequired(
            cfg["PANCAKE_TENANT_SLUG"],
            "pancake_bootstrap_tenant_slug_required",
            64);
        var configuredPlatform = NormalizePlatform(cfg["PANCAKE_PLATFORM"]);

        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>()
            .CreateLogger("PancakeBootstrapSeeder");

        try
        {
            var tenant = await db.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    tenant => tenant.Slug == tenantSlug,
                    ct)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "pancake_bootstrap_tenant_not_found");
            var tokenService = sp.GetRequiredService<IPancakePageTokenService>();

            if (userToken is not null)
            {
                var page = await ResolveListedPageAsync(
                    sp.GetRequiredService<IPageListGateway>(),
                    userToken,
                    pageId,
                    configuredPlatform,
                    ct).ConfigureAwait(false);
                await tokenService.MintAndStoreAsync(
                    tenant.Id,
                    pageId,
                    string.IsNullOrWhiteSpace(page.Name) ? "Bootstrapped" : page.Name.Trim(),
                    page.Platform,
                    userToken,
                    ct).ConfigureAwait(false);
                LogBootstrapped(logger, pageId, page.Platform, viaMint: true);
                return;
            }

            var platform = configuredPlatform
                ?? throw new InvalidOperationException(
                    "pancake_bootstrap_platform_required");
            await tokenService.StorePageTokenDirectAsync(
                tenant.Id,
                pageId,
                "Bootstrapped",
                platform,
                pageToken!,
                ct).ConfigureAwait(false);
            LogBootstrapped(logger, pageId, platform, viaMint: false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogBootstrapFailed(logger, ex, pageId);
            throw;
        }
    }

    private static async Task<PancakePageSummary> ResolveListedPageAsync(
        IPageListGateway pageList,
        string userToken,
        string pageId,
        string? configuredPlatform,
        CancellationToken ct)
    {
        var pages = await pageList.ListAsync(userToken, ct).ConfigureAwait(false);
        var matches = pages
            .Where(page => string.Equals(
                page.PageId.Trim(),
                pageId,
                StringComparison.Ordinal))
            .Select(page => page with
            {
                Platform = NormalizePlatform(page.Platform)
                    ?? throw new InvalidOperationException(
                        "pancake_bootstrap_page_platform_missing"),
            })
            .Where(page => configuredPlatform is null
                || string.Equals(
                    page.Platform,
                    configuredPlatform,
                    StringComparison.Ordinal))
            .Take(2)
            .ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                "pancake_bootstrap_page_not_found"),
            _ => throw new InvalidOperationException(
                "pancake_bootstrap_page_ambiguous"),
        };
    }

    private static string? NormalizeCredential(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        return normalized.StartsWith("replace-with-", StringComparison.OrdinalIgnoreCase)
            || normalized is "changeme" or "change-me" or "replace-me"
            || string.Equals(normalized, "your-token", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "your-access-token", StringComparison.OrdinalIgnoreCase)
            ? null
            : normalized;
    }

    private static string NormalizeRequired(
        string? value,
        string errorCode,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(errorCode);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new InvalidOperationException(errorCode);
        return normalized;
    }

    private static string? NormalizePlatform(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
            return null;
        var normalized = platform.Trim().ToLowerInvariant();
        if (normalized.Length > 32)
        {
            throw new InvalidOperationException(
                "pancake_bootstrap_platform_invalid");
        }
        return normalized;
    }

    [LoggerMessage(EventId = 6020, Level = LogLevel.Information, Message = "PancakeBootstrapSeeder: stored page token for page {pageId}, platform {platform} (viaMint={viaMint})")]
    private static partial void LogBootstrapped(
        ILogger logger,
        string pageId,
        string platform,
        bool viaMint);

    [LoggerMessage(EventId = 6022, Level = LogLevel.Critical, Message = "PancakeBootstrapSeeder: configured bootstrap failed for page {pageId}; startup must stop")]
    private static partial void LogBootstrapFailed(
        ILogger logger,
        Exception ex,
        string pageId);
}
