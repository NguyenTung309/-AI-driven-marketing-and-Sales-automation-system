using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class PancakeWebhookSubscribeScriptTests
{
    [Fact]
    public void Pancake_webhook_subscription_script_is_env_driven_and_dry_runnable()
    {
        var script = File.ReadAllText(FindRepoFile("deploy", "pancake-webhook-subscribe.ps1"));

        script.Should().Contain("PANCAKE_BASE_URL");
        script.Should().Contain("PANCAKE_ACCESS_TOKEN");
        script.Should().Contain("PANCAKE_PAGE_ID");
        script.Should().Contain("PANCAKE_TENANT_SLUG");
        script.Should().Contain("CLAWBOT_PUBLIC_BASE_URL");
        script.Should().Contain("PANCAKE_WEBHOOK_SECRET");
        script.Should().Contain("PANCAKE_SUBSCRIBE_PATH");
        script.Should().Contain("-DryRun");
        script.Should().Contain("/webhooks/pancake/{tenantSlug}");
        script.Should().NotContain("TODO");
    }

    [Fact]
    public void Pancake_webhook_replay_script_signs_sample_payload_and_supports_dry_run()
    {
        var script = File.ReadAllText(FindRepoFile("deploy", "pancake-webhook-replay.ps1"));

        script.Should().Contain("CLAWBOT_PUBLIC_BASE_URL");
        script.Should().Contain("PANCAKE_TENANT_SLUG");
        script.Should().Contain("PANCAKE_WEBHOOK_SECRET");
        script.Should().Contain("PANCAKE_WEBHOOK_PAYLOAD");
        script.Should().Contain("PANCAKE_SIGNATURE_HEADER");
        script.Should().Contain("PANCAKE_SIGNATURE_ENCODING");
        script.Should().Contain("HMACSHA256");
        script.Should().Contain("ConvertTo-Hex");
        script.Should().Contain("Convert.ToBase64String");
        script.Should().Contain("Mask-Signature");
        script.Should().Contain("$safeHeaders[$signatureHeader] = Mask-Signature $signature");
        script.Should().Contain("-DryRun");
        script.Should().Contain("/webhooks/pancake/$tenantSlug");
        script.Should().Contain("Invoke-RestMethod");
        script.Should().NotContain("$headers | ConvertTo-Json -Depth 4");
        script.Should().NotContain("TODO");
    }

    private static string FindRepoFile(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate repo file.", Path.Combine(segments));
    }
}
