using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Channels.Pancake;

// SPEC-16 Module M-1: startup bootstrap that moves the Pancake page token OUT of appsettings.json into the
// encrypted pancake_pages store. Reads the token from env vars (PANCAKE_PAGE_ACCESS_TOKEN or PANCAKE_USER_ACCESS_TOKEN)
// at boot, stores it encrypted for the default tenant, then the env var can be dropped (page tokens never expire).
// Idempotent: re-running only overwrites the stored encrypted token. No-op when env vars are absent.
public static partial class PancakeBootstrapSeeder
{
    public static async Task BootstrapAsync(IServiceProvider services, IConfiguration cfg, CancellationToken ct = default)
    {
        // .NET IConfiguration reads env vars automatically; these keys also accept the Channels__Pancake__ style.
        var pageToken = cfg["PANCAKE_PAGE_ACCESS_TOKEN"];
        var userToken = cfg["PANCAKE_USER_ACCESS_TOKEN"];
        var pageId = cfg["PANCAKE_PAGE_ID"];

        // Nothing to bootstrap — env vars not set; the admin connect flow (M-4) is the runtime path instead.
        if (string.IsNullOrWhiteSpace(pageId) || (string.IsNullOrWhiteSpace(pageToken) && string.IsNullOrWhiteSpace(userToken)))
            return;

        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("PancakeBootstrapSeeder");

        var tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Slug == "default", ct).ConfigureAwait(false);
        if (tenant is null)
        {
            LogNoDefaultTenant(logger);
            return;
        }

        var service = sp.GetRequiredService<IPancakePageTokenService>();
        var pageIdValue = pageId!.Trim();

        // EARS[WHEN a user access token env var is present THE SYSTEM SHALL mint + store a page token from it;
        // WHEN only a page token env var is present THE SYSTEM SHALL store it directly without minting]
        try
        {
            if (!string.IsNullOrWhiteSpace(userToken))
            {
                await service.MintAndStoreAsync(tenant.Id, pageIdValue, name: "Bootstrapped", platform: "pancake", userToken!.Trim(), ct).ConfigureAwait(false);
                LogBootstrapped(logger, pageIdValue, viaMint: true);
            }
            else
            {
                await service.StorePageTokenDirectAsync(tenant.Id, pageIdValue, name: "Bootstrapped", platform: "pancake", pageToken!.Trim(), ct).ConfigureAwait(false);
                LogBootstrapped(logger, pageIdValue, viaMint: false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort: a failed bootstrap must not crash startup — the admin can use the connect flow instead.
            LogBootstrapFailed(logger, ex, pageIdValue);
        }
    }

    [LoggerMessage(EventId = 6020, Level = LogLevel.Information, Message = "PancakeBootstrapSeeder: stored page token for page {pageId} (viaMint={viaMint})")]
    private static partial void LogBootstrapped(ILogger logger, string pageId, bool viaMint);

    [LoggerMessage(EventId = 6021, Level = LogLevel.Warning, Message = "PancakeBootstrapSeeder: no 'default' tenant found; skipping")]
    private static partial void LogNoDefaultTenant(ILogger logger);

    [LoggerMessage(EventId = 6022, Level = LogLevel.Warning, Message = "PancakeBootstrapSeeder: failed for page {pageId}")]
    private static partial void LogBootstrapFailed(ILogger logger, Exception ex, string pageId);
}
