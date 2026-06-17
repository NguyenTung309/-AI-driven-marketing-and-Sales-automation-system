using FluentAssertions;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Notifications;

public sealed class ContentNotifierRegistrationTests
{
    [Fact]
    public void Api_registers_publishing_content_notifier()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "src", "api", "Clawbot.Api", "Program.cs"));

        program.Should().Contain("AddScoped<SignalRContentNotifier>");
        program.Should().Contain("PublishingContentNotifier");
        program.Should().Contain("AddScoped<IContentNotifier>");
    }

    [Fact]
    public void Anomaly_alert_job_routes_notifications_through_content_notifier()
    {
        var root = FindRepositoryRoot();
        var job = File.ReadAllText(Path.Combine(root, "src", "shared", "Clawbot.Infrastructure", "Jobs", "AnomalyAlertJob.cs"));

        job.Should().NotContain("INotificationPublisher");
        job.Should().NotContain("_publisher");
        job.Should().NotContain("NotificationRequest");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Clawbot.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
