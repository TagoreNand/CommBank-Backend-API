using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;

namespace CommBank.Resilience;

/// <summary>
/// Polly policy factory for outbound HTTP dependencies. Combines exponential backoff retry (with jitter),
/// a circuit breaker that sheds load when a dependency is failing, and a per-attempt timeout. Composed onto
/// a named HttpClient so any external call (e.g. a hosted-LLM provider) inherits the resilience for free.
/// </summary>
public static class ResiliencePolicies
{
    /// <summary>Retry transient failures (5xx, 408, 429, timeouts) with exponential backoff + jitter.</summary>
    public static IAsyncPolicy<HttpResponseMessage> Retry(ILogger logger) =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .Or<TimeoutRejectedException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000)),
                onRetry: (outcome, delay, attempt, _) =>
                    logger.LogWarning(
                        "HTTP retry {Attempt} in {Delay}ms due to {Reason}",
                        attempt, delay.TotalMilliseconds,
                        outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()));

    /// <summary>Break the circuit after consecutive transient failures to give the dependency time to recover.</summary>
    public static IAsyncPolicy<HttpResponseMessage> CircuitBreaker(ILogger logger) =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (outcome, breakDelay) =>
                    logger.LogError(
                        "Circuit opened for {Seconds}s due to {Reason}",
                        breakDelay.TotalSeconds,
                        outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()),
                onReset: () => logger.LogInformation("Circuit reset; dependency healthy again."),
                onHalfOpen: () => logger.LogInformation("Circuit half-open; probing dependency."));

    /// <summary>Optimistic per-attempt timeout (relies on cooperative cancellation of the HttpClient).</summary>
    public static IAsyncPolicy<HttpResponseMessage> Timeout() =>
        Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(10));
}
