using System.Net;
using Polly;
using Polly.Extensions.Http;

namespace Clawbot.Infrastructure.Resilience;

public static class HttpResiliencePolicies
{
    // Retries transient HTTP errors AND HTTP 429 (rate limit) with exponential backoff —
    // dành cho connector gọi API vendor bên ngoài vốn hay bị throttle.
    public static IAsyncPolicy<HttpResponseMessage> Retry() =>
        HttpPolicyExtensions.HandleTransientHttpError()
            .OrResult(r => r.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)));

    public static IAsyncPolicy<HttpResponseMessage> CircuitBreaker() =>
        HttpPolicyExtensions.HandleTransientHttpError()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));

    public static IAsyncPolicy<HttpResponseMessage> Timeout(TimeSpan timeout) =>
        Policy.TimeoutAsync<HttpResponseMessage>(timeout);
}
