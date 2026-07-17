using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class FailedMessageRetryEndpointContractTests
{
    [Fact]
    public void Retry_endpoint_enforces_permission_scope_and_atomic_claim()
    {
        var endpoints = File.ReadAllText(FindRepoFile(
            "src", "api", "Clawbot.Api", "Endpoints", "InboxEndpoints.cs"));
        var service = File.ReadAllText(FindRepoFile(
            "src", "api", "Clawbot.Api", "Services", "FailedMessageRetryService.cs"));

        endpoints.Should().Contain("/conversations/{id:guid}/messages/{messageId:guid}/retry");
        endpoints.Should().Contain("RequirePermission(\"conversations:write\")");
        endpoints.Should().Contain("IsOutsideInboxScope(inboxIds, conversation.InboxId)");
        endpoints.Should().Contain("c.Id == id && c.TenantId == tenant.TenantId");

        service.Should().Contain("ExecuteUpdateAsync");
        service.Should().Contain("m.Status == \"send_failed\"");
        service.Should().Contain("m.ExternalMessageId == null");
        service.Should().Contain("\"message:retry\"");
        service.Should().Contain("CancellationToken.None");
        service.Should().NotContain("AppendMessage(");
    }

    private static string FindRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, segments));
    }
}
