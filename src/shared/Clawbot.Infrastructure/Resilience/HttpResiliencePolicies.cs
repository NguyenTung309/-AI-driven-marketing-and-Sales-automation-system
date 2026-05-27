using Polly;
using Polly.Extensions.Http;

namespace Clawbot.Infrastructure.Resilience;

public static class HttpResiliencePolicies
{
    public static IAsyncPolicy<HttpResponseMessage> Retry() =>
        HttpPolicyExtensions.HandleTransientHttpError()
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)));

    public static IAsyncPolicy<HttpResponseMessage> CircuitBreaker() =>
        HttpPolicyExtensions.HandleTransientHttpError()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));

    public static IAsyncPolicy<HttpResponseMessage> Timeout(TimeSpan timeout) =>
        Policy.TimeoutAsync<HttpResponseMessage>(timeout);
}
