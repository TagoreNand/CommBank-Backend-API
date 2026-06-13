using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CommBank.Observability;

/// <summary>
/// Registers OpenTelemetry tracing + metrics and the health-check suite. Tracing covers ASP.NET Core,
/// outbound HttpClient and MongoDB driver commands plus the app's own <see cref="AppDiagnostics.ActivitySource"/>;
/// metrics expose ASP.NET Core, HttpClient and the app's custom <see cref="AppDiagnostics.Meter"/> via Prometheus.
/// </summary>
public static class ObservabilityServiceCollectionExtensions
{
    private const string MongoDriverActivitySource = "MongoDB.Driver.Core.Extensions.DiagnosticSources";

    public static IServiceCollection AddCommBankObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        string? otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];

        ResourceBuilder resource = ResourceBuilder.CreateDefault()
            .AddService(serviceName: AppDiagnostics.ServiceName, serviceVersion: AppDiagnostics.ServiceVersion)
            .AddAttributes(new KeyValuePair<string, object>[]
            {
                new("deployment.environment", environment.EnvironmentName)
            });

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resource)
                    .AddSource(AppDiagnostics.ActivitySourceName)
                    .AddSource(MongoDriverActivitySource)
                    .AddAspNetCoreInstrumentation(o => o.RecordException = true)
                    .AddHttpClientInstrumentation();

                if (environment.IsDevelopment())
                {
                    tracing.AddConsoleExporter();
                }

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    // OTLP/gRPC by default (e.g. Jaeger's collector on :4317).
                    tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
                }
            })
            .WithMetrics(metrics => metrics
                .SetResourceBuilder(resource)
                .AddMeter(AppDiagnostics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddPrometheusExporter());

        services.AddHealthChecks()
            .AddCheck<MongoHealthCheck>("mongodb", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready" });

        return services;
    }
}
