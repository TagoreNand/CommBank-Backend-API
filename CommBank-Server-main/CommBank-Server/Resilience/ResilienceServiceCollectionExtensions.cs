using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace CommBank.Resilience;

/// <summary>
/// Registers outbound HTTP resilience (a named, Polly-wrapped HttpClient) and inbound rate limiting using
/// the framework rate limiter (net8). Requests are partitioned per client IP with a fixed window;
/// health and metrics endpoints are exempt.
/// </summary>
public static class ResilienceServiceCollectionExtensions
{
    /// <summary>Name of the resilient HttpClient: <c>services.AddHttpClient(ResilientClientName)</c>.</summary>
    public const string ResilientClientName = "resilient";

    public static IServiceCollection AddCommBankResilience(this IServiceCollection services, IConfiguration configuration)
    {
        // Outbound HTTP resilience: retry -> circuit breaker -> timeout (outer to inner).
        services.AddHttpClient(ResilientClientName)
            .AddPolicyHandler((provider, _) =>
                ResiliencePolicies.Retry(provider.GetRequiredService<ILoggerFactory>().CreateLogger("Resilience.Retry")))
            .AddPolicyHandler((provider, _) =>
                ResiliencePolicies.CircuitBreaker(provider.GetRequiredService<ILoggerFactory>().CreateLogger("Resilience.CircuitBreaker")))
            .AddPolicyHandler(ResiliencePolicies.Timeout());

        int permitLimit = configuration.GetValue("RateLimiting:PermitLimit", 200);
        int windowSeconds = configuration.GetValue("RateLimiting:WindowSeconds", 60);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                string path = context.Request.Path.Value ?? string.Empty;

                // Never throttle health/metrics scrapes.
                if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase))
                {
                    return RateLimitPartition.GetNoLimiter("infra");
                }

                string clientKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(clientKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromSeconds(windowSeconds),
                    QueueLimit = 0
                });
            });
        });

        return services;
    }
}
