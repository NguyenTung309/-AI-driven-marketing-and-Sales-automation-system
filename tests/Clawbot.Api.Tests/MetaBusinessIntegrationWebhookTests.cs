using System.Security.Cryptography;
using System.Text;
using Clawbot.Api.Endpoints;
using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.Infrastructure.Jobs;
using FluentAssertions;
using Xunit;

namespace Clawbot.Api.Tests;

public sealed class MetaBusinessIntegrationWebhookTests
{
    [Fact]
    public void Signature_validation_uses_x_hub_sha256_over_the_raw_payload()
    {
        var payload = Encoding.UTF8.GetBytes("""{"object":"application"}""");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("app-secret"));
        var signature = $"sha256={Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant()}";

        MetaBusinessIntegrationWebhookEndpoints.IsValidSignature(payload, signature, "app-secret").Should().BeTrue();
        MetaBusinessIntegrationWebhookEndpoints.IsValidSignature(payload, signature, "other-secret").Should().BeFalse();
    }

    [Fact]
    public void ParseChanges_accepts_only_supported_fields_for_the_configured_app()
    {
        var payload = Encoding.UTF8.GetBytes("""
            {
              "object":"application",
              "entry":[
                {
                  "id":"app-123",
                  "changes":[
                    {"field":"business_integration_update","value":{"business_manager_id":"business-1"}},
                    {"field":"business_integration_update","value":{"business_manager_id":"business-1"}},
                    {"field":"unrelated","value":{"business_manager_id":"business-1"}}
                  ]
                },
                {
                  "id":"another-app",
                  "changes":[
                    {"field":"business_integration_uninstall","value":{"business_manager_id":"business-2"}}
                  ]
                }
              ]
            }
            """);

        var changes = MetaBusinessIntegrationWebhookEndpoints.ParseChanges(payload, "app-123");

        var change = changes.Should().ContainSingle().Which;
        change.Field.Should().Be(MetaBusinessIntegrationWebhookJob.UpdateField);
        change.BusinessManagerId.Should().Be("business-1");
    }

    [Fact]
    public void ParseApplicationIds_selects_the_app_before_signature_routing()
    {
        var payload = Encoding.UTF8.GetBytes("""
            {
              "object":"application",
              "entry":[{"id":"app-123","changes":[]},{"id":"app-456","changes":[]}]
            }
            """);

        MetaBusinessIntegrationWebhookEndpoints.ParseApplicationIds(payload)
            .Should().BeEquivalentTo(["app-123", "app-456"]);
    }

    [Fact]
    public void MatchConfigurations_keeps_all_tenants_that_share_the_signed_app()
    {
        var firstTenant = Guid.NewGuid();
        var secondTenant = Guid.NewGuid();
        var options = new MetaGraphOptions { AppId = "shared-app" };
        var candidates = new[]
        {
            new MetaGraphConfigurationCandidate(firstTenant, options),
            new MetaGraphConfigurationCandidate(secondTenant, options),
            new MetaGraphConfigurationCandidate(Guid.NewGuid(), new MetaGraphOptions { AppId = "other-app" }),
        };

        MetaBusinessIntegrationWebhookEndpoints.MatchConfigurations(
                candidates,
                new HashSet<string>(["shared-app"], StringComparer.Ordinal))
            .Select(x => x.TenantId)
            .Should().BeEquivalentTo([firstTenant, secondTenant]);
    }
}
