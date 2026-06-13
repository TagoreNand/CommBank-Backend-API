# CommBank Intelligence Module (`CommBank.AI`)

An in-process, decoupled AI/ML layer for the CommBank backend. Every capability sits behind a **port**
(interface) with a swappable **adapter**, so models can evolve (heuristic → ML.NET → remote/LLM) without
touching the money path or any controller.

## Capabilities

| Capability | Port | Default adapter |
| --- | --- | --- |
| Fraud / AML risk scoring | `IRiskScoringService` | `HeuristicRiskScoringService` |
| Transaction auto-categorization | `ICategorizationService` | `RuleBasedCategorizationService` |
| Goal forecasting & spending insights | `IGoalForecastingService` | `GoalForecastingService` |
| Natural-language assistant | `IFinancialAssistantService` | `FinancialAssistantService` (local) |
| Hosted-LLM seam | `ILanguageModelClient` | `NullLanguageModelClient` (no-op) |
| Immutable decision audit | `IMlDecisionAuditService` | `MongoMlDecisionAuditService` |
| Risk-aware create workflow | `IRiskAwareTransactionOrchestrator` | `RiskAwareTransactionOrchestrator` |

## Design principles

- **Decoupled from the core.** Controllers and the orchestrator depend only on interfaces. Replacing a
  model is a one-line change in `AddCommBankIntelligence`.
- **Deterministic & explainable.** The risk scorer emits weighted, audit-grade reason codes
  (`AMOUNT_CRITICAL`, `VELOCITY`, `STRUCTURING`, `RAPID_REPEAT`, …); the same inputs always produce the
  same score. Feature engineering is isolated in `TransactionFeatureExtractor`, ready to feed an ML.NET model.
- **Resilience first.** Scoring never throws for ordinary inputs. The orchestrator applies a
  **fail-open** policy for low-value transactions and **fail-closed** (block) at/above
  `FailClosedAmountThreshold`, so the system stays both available and safe.
- **Idempotent.** `RiskAwareTransactionOrchestrator` checks the audit trail by `Idempotency-Key` and
  replays the prior decision instead of double-creating on retries.
- **Auditable.** Every decision is appended (never updated/deleted) to the `MlDecisions` collection with
  the model version, score, reason JSON and a SHA-256 of the feature vector (no raw PII).
- **Privacy-aware.** The assistant answers only from the requesting user's own records. The optional LLM
  fallback receives aggregate-only, PII-masked context.

## API surface (`/api/Intelligence`)

| Method & route | Purpose |
| --- | --- |
| `POST /risk/score` | Score a transaction for fraud/AML risk **without** persisting. |
| `POST /transactions` | Risk-aware create: score → persist or block. Honours the `Idempotency-Key` header. `201` created, `202` created+flagged, `422` blocked. |
| `POST /categorize` | Suggest tags for a transaction against the tenant taxonomy. |
| `GET /goals/{id}/forecast` | Completion forecast for a savings goal. |
| `GET /users/{id}/insights` | Aggregate spending insights and anomalies. |
| `POST /assistant` | Natural-language Q&A scoped to a user. |

> **Security note:** these endpoints are currently unauthenticated, matching the rest of the API. They
> must sit behind the authentication/authorization work tracked in the project backlog before any
> non-local deployment.

## Configuration (`appsettings.json` → `Ai`)

Thresholds, model versions, fail-mode and the audit collection are all configurable; only non-secret
values live here. Any hosted-LLM API key is resolved from environment variables / user-secrets, never
from committed config.

## Swapping in a real model

- **ML.NET risk model:** implement `IRiskScoringService` (reusing `TransactionFeatureExtractor`) and
  register it in place of `HeuristicRiskScoringService`.
- **Remote scorer / Python microservice:** implement the same port with an `HttpClient` + circuit
  breaker + timeout; nothing else changes.
- **Hosted LLM assistant:** implement `ILanguageModelClient` (Anthropic/OpenAI) with its own timeout,
  retry and key handling, set `Ai:Assistant:Provider`, and register it instead of `NullLanguageModelClient`.

## Tests

`CommBank.Tests/AI` contains deterministic unit tests for the risk engine (approve/block/rapid-repeat/
determinism), the categorizer, and the forecaster (on-track + anomaly detection).
