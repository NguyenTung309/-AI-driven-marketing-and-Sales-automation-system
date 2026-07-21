using FluentAssertions;

namespace Clawbot.Infrastructure.Tests.Content;

public sealed class ContentAutoSchedulerContractTests
{
    [Fact]
    public void Approval_scheduler_uses_golden_hour_and_current_content_revision()
    {
        var source = File.ReadAllText(FindRepoFile(
            "src",
            "shared",
            "Clawbot.Infrastructure",
            "Content",
            "ContentAutoScheduler.cs"));

        source.Should().Contain("IGoldenHourResolver");
        source.Should().Contain("ResolveNext(");
        source.Should().Contain("ContentSchedule.Schedule(");
        source.Should().Contain("item.ContentRevision");
    }

    private static string FindRepoFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(segments)}");
    }
}
