using System.Text.Json;
using CommBank.Models;
using CommBank.Services;
using CommBank.AI.DependencyInjection;
using CommBank.Auth;
using CommBank.Transfers;
using CommBank.Ledger;
using CommBank.Observability;
using CommBank.Resilience;
using Asp.Versioning;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Driver;
using MongoDB.Driver.Core.Extensions.DiagnosticSources;
using OpenTelemetry;
using Serilog;
using Serilog.Formatting.Compact;

// Bootstrap logger: captures anything that fails before the host (and its real logger) is built.
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Replace the default logging stack with Serilog (structured JSON to stdout).
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", AppDiagnostics.ServiceName)
        .WriteTo.Console(new CompactJsonFormatter()));

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    // API versioning: default v1.0, assumed when unspecified; selected via ?api-version= or X-Api-Version.
    builder.Services
        .AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
            options.ApiVersionReader = ApiVersionReader.Combine(
                new QueryStringApiVersionReader("api-version"),
                new HeaderApiVersionReader("X-Api-Version"));
        })
        .AddApiExplorer(options => options.GroupNameFormat = "'v'VVV");

    // Request validation via FluentValidation (auto-validates [ApiController] actions -> 400 ProblemDetails).
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();

    // Swagger with a JWT bearer scheme so protected endpoints are callable from the UI.
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo { Title = "CommBank API", Version = "v1" });

        var scheme = new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste a JWT access token (the raw token, without the 'Bearer ' prefix)."
        };
        options.AddSecurityDefinition("Bearer", scheme);
        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                },
                Array.Empty<string>()
            }
        });
    });

    // ---------------------------------------------------------------------------
    // Secure configuration loading. Precedence (lowest -> highest):
    //   appsettings -> optional Secrets.json -> user-secrets (dev) -> environment variables (authoritative).
    // ---------------------------------------------------------------------------
    builder.Configuration.AddJsonFile("Secrets.json", optional: true, reloadOnChange: true);

    if (builder.Environment.IsDevelopment())
    {
        builder.Configuration.AddUserSecrets<Program>(optional: true);
    }

    builder.Configuration.AddEnvironmentVariables();

    // Fail fast: a banking service must never start without a validated datastore credential.
    var mongoConnectionString = builder.Configuration.GetConnectionString("CommBank");
    if (string.IsNullOrWhiteSpace(mongoConnectionString) ||
        mongoConnectionString.Contains("REPLACE_ME", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "Missing or placeholder MongoDB connection string 'ConnectionStrings:CommBank'. " +
            "Supply it via the environment variable 'ConnectionStrings__CommBank', .NET user-secrets " +
            "(dotnet user-secrets set \"ConnectionStrings:CommBank\" \"<value>\"), or a local, gitignored " +
            "Secrets.json. Never commit live credentials to source control.");
    }

    // Instrument the Mongo driver so its commands surface as spans in distributed traces.
    var mongoSettings = MongoClientSettings.FromConnectionString(mongoConnectionString);
    mongoSettings.ClusterConfigurator = cb => cb.Subscribe(new DiagnosticsActivityEventSubscriber());
    var mongoClient = new MongoClient(mongoSettings);
    var mongoDatabase = mongoClient.GetDatabase("CommBank");

    builder.Services.AddSingleton<IMongoClient>(mongoClient);
    builder.Services.AddSingleton(mongoDatabase);

    // Domain persistence services (Mongo-backed), registered by interface — no hand-rolled singletons.
    builder.Services.AddCommBankPersistence();

    // AI/ML intelligence module.
    builder.Services.AddCommBankIntelligence(builder.Configuration);

    // Fund-transfer module: ACID multi-document transfers with optimistic concurrency + idempotency.
    builder.Services.AddCommBankTransfers(builder.Configuration);

    // Double-entry ledger + transactional outbox (with a background publishing relay).
    builder.Services.AddCommBankLedger();

    // JWT bearer authentication + authorization. Fails fast if the signing key is missing/weak.
    builder.Services.AddCommBankJwtAuth(builder.Configuration);

    // Observability (OpenTelemetry tracing + metrics + health checks).
    builder.Services.AddCommBankObservability(builder.Configuration, builder.Environment);

    // Resilience (Polly HTTP policies + IP rate limiting).
    builder.Services.AddCommBankResilience(builder.Configuration);

    // CORS: permissive in Development; restricted to configured origins everywhere else.
    var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            if (builder.Environment.IsDevelopment())
            {
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
            }
            else if (corsOrigins.Length > 0)
            {
                policy.WithOrigins(corsOrigins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
            }
        });
    });

    var app = builder.Build();

    // ----- Middleware pipeline (order is significant) -----
    app.UseMiddleware<ExceptionHandlingMiddleware>();   // outermost: convert anything uncaught into ProblemDetails
    app.UseMiddleware<CorrelationIdMiddleware>();        // assign correlation id before request logging
    app.UseSerilogRequestLogging();                      // one structured summary log per request

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();
    app.UseRateLimiter();                                // 429 before any expensive work
    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    // Prometheus scrape endpoint (/metrics) + Kubernetes-style liveness/readiness probes.
    app.UseOpenTelemetryPrometheusScrapingEndpoint();
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("ready"),
        ResponseWriter = WriteHealthResponse
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "CommBank API terminated unexpectedly during startup.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// Writes a compact JSON body for the readiness probe.
static Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";

    var payload = new
    {
        status = report.Status.ToString(),
        totalDurationMs = report.TotalDuration.TotalMilliseconds,
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            description = entry.Value.Description,
            durationMs = entry.Value.Duration.TotalMilliseconds
        })
    };

    return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
}
