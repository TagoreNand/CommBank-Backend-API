using System.Diagnostics;
using Serilog.Context;

namespace CommBank.Observability;

/// <summary>
/// Ensures every request carries a correlation id: it reuses an inbound <c>X-Correlation-ID</c> header or
/// mints one, echoes it on the response, tags the active trace span, and pushes it onto the Serilog
/// LogContext so every log line for the request is correlatable end-to-end.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId =
            context.Request.Headers.TryGetValue(HeaderName, out var inbound) && !string.IsNullOrWhiteSpace(inbound)
                ? inbound.ToString()
                : Guid.NewGuid().ToString("N");

        context.Items[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        Activity.Current?.SetTag("correlation_id", correlationId);

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
