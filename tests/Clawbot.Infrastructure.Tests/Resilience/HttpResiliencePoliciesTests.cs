using System.Net;
using Clawbot.Infrastructure.Resilience;
using FluentAssertions;

namespace Clawbot.Infrastructure.Tests.Resilience;

public sealed class HttpResiliencePoliciesTests
{
    [Fact]
    public async Task Retry_retries_rate_limited_responses_three_times_before_success()
    {
        var attempts = 0;
        var policy = HttpResiliencePolicies.Retry();

        using var response = await policy.ExecuteAsync(() =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(
                attempts < 4 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK));
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        attempts.Should().Be(4);
    }
}
