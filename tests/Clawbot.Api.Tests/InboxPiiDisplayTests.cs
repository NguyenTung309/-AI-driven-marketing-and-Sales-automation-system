using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class InboxPiiDisplayTests
{
    [Fact]
    public void Conversation_detail_prefers_retained_original_content_for_authorized_display()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "InboxEndpoints.cs"));

        source.Should().Contain("m.OriginalContent ?? m.RedactedContent ?? m.Content");
    }

    [Fact]
    public void Public_widget_persists_raw_and_redacted_content_separately()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "PublicWidgetEndpoints.cs"));

        source.Should().Contain("redactedVisitorText = await pii.RedactAsync(visitorText, ct)");
        source.Should().Contain("originalContent: visitorText");
        source.Should().Contain("redactedContent: redactedVisitorText.RedactedText");
    }

    [Fact]
    public void Conversation_list_preview_remains_redacted()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "InboxEndpoints.cs"));

        source.Should().Contain("c.Messages.OrderByDescending(m => m.SentAt).Select(m => m.Content).FirstOrDefault()");
    }

    [Fact]
    public void Conversation_export_enforces_the_same_inbox_scope_as_detail()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "InboxEndpoints.cs"));

        source.Should().Contain("private static async Task<IResult> ExportCsvAsync(");
        source.Should().Contain("var inboxIds = await resolver.GetInboxIdsAsync(user, ct)");
        source.Should().Contain("IsOutsideInboxScope(inboxIds, conversation.InboxId)");
    }

    [Fact]
    public void Inbox_realtime_routes_events_only_to_authorized_inbox_groups()
    {
        var hub = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Hubs", "InboxHub.cs"));
        var notifier = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Hubs", "SignalRInboxNotifier.cs"));

        hub.Should().Contain("permissions.Contains(\"conversations:read\")");
        hub.Should().Contain("InboxGroup(inboxId)");
        notifier.Should().Contain("InboxHub.InboxGroup(inboxId.Value)");
        notifier.Should().Contain("InboxHub.AdminGroup(tenantId)");
        notifier.Should().NotContain("TenantGroup");
    }

    [Fact]
    public void Conversation_mutations_deny_inboxless_records_for_scoped_users()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "InboxEndpoints.cs"));

        source.Should().NotContain("conv.InboxId.HasValue && !inboxIds.Contains");
        source.Split("IsOutsideInboxScope(inboxIds, conv.InboxId)", StringSplitOptions.None)
            .Length.Should().BeGreaterThan(7);
    }

    [Fact]
    public void Conversation_detail_denies_inboxless_records_for_scoped_users()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "InboxEndpoints.cs"));

        source.Should().Contain("IsOutsideInboxScope(inboxIds, conv.InboxId)");
        source.Should().Contain("inboxIds.Count > 0 && (!inboxId.HasValue || !inboxIds.Contains(inboxId.Value))");
    }

    private static string FindRepoFile(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(segments)}");
    }
}
