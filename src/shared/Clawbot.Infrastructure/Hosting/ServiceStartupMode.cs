using Microsoft.Extensions.Configuration;

namespace Clawbot.Infrastructure.Hosting;

/// <summary>
/// Deployment startup mode. A candidate release starts passive so the deployment can smoke-check
/// it without consuming queues, running schedules, processing jobs, or polling providers. Only
/// after the smoke checks pass does the deployment restart it active and promote the release.
/// </summary>
public static class ServiceStartupMode
{
    public const string ConfigurationKey = "Clawbot:StartupMode";

    private const string ActiveValue = "active";
    private const string PassiveValue = "passive";

    /// <summary>
    /// True when background processing must stay off. An unrecognised value throws rather than
    /// defaulting to active, so a typo cannot silently let an unproven candidate act on its own.
    /// </summary>
    public static bool IsPassive(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var mode = configuration[ConfigurationKey]?.Trim();
        if (string.IsNullOrEmpty(mode) || string.Equals(mode, ActiveValue, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(mode, PassiveValue, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        throw new InvalidOperationException(
            $"{ConfigurationKey} must be '{ActiveValue}' or '{PassiveValue}', but was '{mode}'.");
    }
}
