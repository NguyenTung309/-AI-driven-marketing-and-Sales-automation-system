using Clawbot.Domain.Agents;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Agents;

public sealed class OrchestrationExceptionTests
{
    [Fact]
    public void PublicationInProgress_HasExpectedMessage()
    {
        var ex = new OrchestrationPublicationInProgressException();

        ex.Message.Should().Be("orchestration_publication_in_progress");
    }

    [Fact]
    public void SessionEtagMismatch_HasExpectedMessage()
    {
        var ex = new OrchestrationSessionEtagMismatchException();

        ex.Message.Should().Be("orchestration_session_etag_mismatch");
    }

    [Fact]
    public void SessionNotRunning_HasExpectedMessage()
    {
        var ex = new OrchestrationSessionNotRunningException();

        ex.Message.Should().Be("orchestration_session_not_running");
    }
}
