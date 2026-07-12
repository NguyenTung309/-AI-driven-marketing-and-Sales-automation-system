using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.Infrastructure.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Jobs;

public sealed class MetaBusinessIntegrationWebhookJobTests
{
    [Fact]
    public async Task Update_event_revalidates_and_resynchronizes_the_tenant_connection()
    {
        var tenantId = Guid.NewGuid();
        var integrations = Substitute.For<IMetaIntegrationService>();
        var job = Build(integrations);

        await job.RunAsync(tenantId, MetaBusinessIntegrationWebhookJob.UpdateField);

        await integrations.Received(1).ValidateAsync(tenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Uninstall_event_blocks_the_tenant_connection_immediately()
    {
        var tenantId = Guid.NewGuid();
        var integrations = Substitute.For<IMetaIntegrationService>();
        var job = Build(integrations);

        await job.RunAsync(tenantId, MetaBusinessIntegrationWebhookJob.UninstallField);

        await integrations.Received(1).MarkReconnectRequiredAsync(
            tenantId,
            "meta_business_integration_uninstalled",
            Arg.Any<CancellationToken>());
        await integrations.DidNotReceiveWithAnyArgs().ValidateAsync(default);
    }

    private static MetaBusinessIntegrationWebhookJob Build(IMetaIntegrationService integrations) =>
        new(
            integrations,
            NullLogger<MetaBusinessIntegrationWebhookJob>.Instance);
}
