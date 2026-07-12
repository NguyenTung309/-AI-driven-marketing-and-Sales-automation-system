using Clawbot.Infrastructure.Integrations.Meta;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class MetaBusinessIntegrationWebhookJob(
    IMetaIntegrationService integrations,
    ILogger<MetaBusinessIntegrationWebhookJob> logger)
{
    public const string InstallField = "business_integration_install";
    public const string UpdateField = "business_integration_update";
    public const string UninstallField = "business_integration_uninstall";

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 300, 900])]
    public async Task RunAsync(Guid tenantId, string field, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return;

        if (string.Equals(field, UninstallField, StringComparison.Ordinal))
        {
            await integrations.MarkReconnectRequiredAsync(
                tenantId,
                "meta_business_integration_uninstalled",
                ct).ConfigureAwait(false);
            return;
        }

        if (field is not (InstallField or UpdateField))
            return;

        try
        {
            await integrations.ValidateAsync(tenantId, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            LogConnectionNotReady(logger, tenantId, field);
        }
    }

    [LoggerMessage(EventId = 5521, Level = LogLevel.Debug, Message = "Meta business integration webhook {Field} arrived before tenant {TenantId} had a stored connection")]
    private static partial void LogConnectionNotReady(ILogger logger, Guid tenantId, string field);
}
