# Observability & Resilience

Production-readiness layer covering **pillar 4 (observability & auditability)** and
**pillar 5 (resilience & fault tolerance)**.

## Observability

### Structured logging (Serilog)
- All logs are emitted as **compact JSON** to stdout (container/Loki/ELK friendly).
- A **bootstrap logger** captures startup failures before the host is built; the program is wrapped in
  `try/catch/finally` so a fatal startup error is logged and flushed.
- `UseSerilogRequestLogging` writes one structured summary per request (method, path, status, elapsed).
- Levels are configured under `Serilog:MinimumLevel` in `appsettings.json`.

### Correlation IDs
`CorrelationIdMiddleware` reuses an inbound `X-Correlation-ID` or mints one, echoes it on the response,
tags the active trace span, and pushes it onto the Serilog `LogContext` so **every** log line for a
request is correlatable. It is also surfaced on error responses.

### Distributed tracing (OpenTelemetry)
- Instruments **ASP.NET Core**, outbound **HttpClient**, and the **MongoDB driver** (commands appear as
  spans via `DiagnosticsActivityEventSubscriber`), plus the app's own `AppDiagnostics.ActivitySource`.
- Exports to the **console** by default; swap in OTLP for Jaeger/Tempo by adding `.AddOtlpExporter()`.
- Resource attributes include service name/version and `deployment.environment`.

### Metrics (OpenTelemetry → Prometheus)
- ASP.NET Core + HttpClient instrumentation, plus custom business metrics from `AppDiagnostics.Meter`:
  `commbank.transfers.completed`, `commbank.transfers.blocked`, `commbank.transfers.amount`.
- Scraped at **`GET /metrics`** (Prometheus exposition format).

### Health checks
- **`GET /health/live`** — liveness (process is up; runs no checks).
- **`GET /health/ready`** — readiness; includes a custom `MongoHealthCheck` (pings the DB) and returns a
  JSON body of each check's status. Used by Kubernetes probes / load balancers.

## Resilience

### Outbound HTTP policies (Polly)
A named **`"resilient"`** `HttpClient` composes (outer→inner): **retry** (3×, exponential backoff with
jitter, on 5xx/408/429/timeouts) → **circuit breaker** (opens after 5 failures for 30s) → **timeout**
(10s). Any external dependency (e.g. a hosted-LLM provider) gets resilience for free by resolving this
client. See `ResiliencePolicies` / `ResilienceServiceCollectionExtensions`.

### Rate limiting (AspNetCoreRateLimit)
IP-based limiting with an in-memory store: defaults of **10 req/s** and **200 req/min** per IP,
configurable under `IpRateLimiting`. Health and metrics endpoints are whitelisted. Exceeding a limit
returns **429** with `Retry-After`.

### Global exception handling
`ExceptionHandlingMiddleware` is the outermost layer: anything uncaught is logged with the trace and
correlation ids and returned as **RFC 7807 ProblemDetails** (full detail in Development, a safe message
+ `traceId` in Production). Domain exceptions handled by controllers never reach it.

## Middleware order (Program.cs)
`ExceptionHandling → CorrelationId → SerilogRequestLogging → [Swagger] → HttpsRedirection →
IpRateLimiting → CORS → Authentication → Authorization → Controllers → /metrics + health probes`.

## Endpoints summary
| Endpoint | Purpose |
| --- | --- |
| `GET /metrics` | Prometheus scrape |
| `GET /health/live` | Liveness probe |
| `GET /health/ready` | Readiness probe (incl. MongoDB) |

## Note on .NET 8
The project targets **net8 LTS**. Rate limiting uses the **built-in** `Microsoft.AspNetCore.RateLimiting`
middleware (`AddRateLimiter` / `UseRateLimiter`) rather than a third-party package, and OpenTelemetry runs
at 1.9.x. The OTel **Prometheus exporter** remains a `-beta` package — the one prerelease dependency.
