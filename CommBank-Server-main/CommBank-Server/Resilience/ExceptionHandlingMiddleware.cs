using System.Diagnostics;
using System.Text.Json;
using CommBank.Observability;
using Microsoft.AspNetCore.Mvc;

namespace CommBank.Resilience;

/// <summary>
/// Outermost safety net. Any exception that escapes the pipeline is logged with the trace/correlation id
/// and returned as an RFC 7807 ProblemDetails (never a raw stack trace in non-Development). Domain
/// exceptions handled explicitly by controllers never reach here; this exists for the unexpected.
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            string traceId = Activity.Current?.Id ?? context.TraceIdentifier;
            _logger.LogError(ex, "Unhandled exception (traceId {TraceId})", traceId);

            if (context.Response.HasStarted)
            {
                // The response is already on the wire; we cannot convert it to a problem document.
                throw;
            }

            var problem = new ProblemDetails
            {
                Title = "An unexpected error occurred.",
                Status = StatusCodes.Status500InternalServerError,
                Type = "https://httpstatuses.io/500",
                Detail = _environment.IsDevelopment()
                    ? ex.ToString()
                    : "An internal server error occurred. Quote the traceId when contacting support."
            };
            problem.Extensions["traceId"] = traceId;

            if (context.Items.TryGetValue(CorrelationIdMiddleware.HeaderName, out var correlationId))
            {
                problem.Extensions["correlationId"] = correlationId?.ToString();
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            await context.Response.WriteAsync(JsonSerializer.Serialize(problem), context.RequestAborted);
        }
    }
}
