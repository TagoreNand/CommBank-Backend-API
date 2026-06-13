# CommBank-Backend-API — Architecture

A modular ASP.NET Core (net8) banking backend on MongoDB, organised by **bounded module** with
**ports & adapters (hexagonal)** boundaries.

## Layering

Each module exposes **ports** (interfaces) and ships **in-process adapters**, so an implementation can be
swapped (heuristic → ML model → remote service; logging publisher → Kafka; etc.) without touching callers.

```
HTTP (Controllers)  ──►  Application services (ports)  ──►  Adapters (Mongo / AI / HTTP)
        │                          │                                 │
   thin edge:                use-case orchestration            infrastructure:
   validation, authz,        (FundTransferService,             MongoDB collections,
   ProblemDetails mapping     orchestrator, ledger)            DiagnosticSources, Polly
```

## Modules

| Module | Folder | Ports → default adapters |
| --- | --- | --- |
| Domain services | `Services/` | `IAccountsService`, `ITransactionsService`, … → Mongo services (registered via `AddCommBankPersistence`) |
| Authentication | `Auth/` | `ITokenService`, `IRefreshTokenService`, `ITotpService` (JWT + refresh rotation + TOTP/step-up) |
| AI / ML | `AI/` | `IRiskScoringService`, `ICategorizationService`, `IGoalForecastingService`, `IFinancialAssistantService`, `ILanguageModelClient`, `IMlDecisionAuditService` |
| Fund transfers | `Transfers/` | `IFundTransferService` (ACID multi-doc transaction + optimistic concurrency + idempotency + risk gate) |
| Ledger & outbox | `Ledger/` | `ILedgerService`, `IOutboxService`, `IEventPublisher`, `OutboxProcessor` |
| Observability | `Observability/` | Serilog, OpenTelemetry, health checks, `AppDiagnostics` |
| Resilience | `Resilience/` | Polly policies, built-in rate limiter, global exception handler |

Each module owns a `*ServiceCollectionExtensions.AddCommBank*` composition root; `Program.cs` is now a thin
wire-up that calls them (no hand-rolled singletons).

## Cross-cutting guarantees (the 5 pillars)

1. **Consistency** — multi-document ACID transactions + optimistic concurrency (`Account.Version`) + a
   double-entry ledger written in the same transaction.
2. **Idempotency** — `Idempotency-Key` on transfers/transactions; transactional outbox for events.
3. **Security** — JWT + refresh-token rotation with reuse detection, RBAC, TOTP MFA + step-up for
   high-value transfers, secrets via env/user-secrets with fail-fast, no PII in logs.
4. **Observability** — structured JSON logs, OpenTelemetry traces (incl. Mongo) + Prometheus metrics,
   health probes, immutable ML/ledger audit trails.
5. **Resilience** — Polly retry/circuit-breaker/timeout, rate limiting, fail-open/closed risk policy,
   ProblemDetails everywhere.

## Target follow-up: physical project split

The module boundaries above are already clean seams. The recommended next step (best done with a compiler
in the loop) is to extract them into separate projects:

```
CommBank.Domain          (entities, value objects, domain events — no framework deps)
CommBank.Application      (ports / use-case services: transfers, ledger, AI orchestration)
CommBank.Infrastructure  (Mongo adapters, OTel, Polly, JWT, outbox relay)
CommBank.Api             (controllers, Program.cs composition)
```

Dependencies point inward (Api → Application → Domain; Infrastructure → Application/Domain), enforced with
an architecture test (NetArchTest). No business logic would move — only file locations and project
references — because the code is already organised by port/adapter today.
