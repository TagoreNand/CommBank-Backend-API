# Fund Transfer Module (`CommBank.Transfers`)

Atomic, idempotent, concurrency-safe money movement — the core banking workflow, demonstrating
**pillar 1 (absolute data consistency)** and **pillar 2 (idempotency & reliability)**.

## Guarantees

- **Atomicity (ACID).** The source debit, the destination credit, both ledger `Transaction`
  documents, the `FundTransfer` audit record and the idempotency entry all commit inside **one**
  MongoDB multi-document transaction (`ReadConcern.Snapshot` / `WriteConcern.WMajority`). A partial
  transfer — money leaving one account without arriving in the other — is impossible.
- **Optimistic concurrency control.** Every `Account` carries a `Version`. Each balance update is
  conditional on the expected version and `$inc`s it; a zero `ModifiedCount` means another writer won,
  which raises `ConcurrencyConflictException` and triggers a bounded retry of the whole transaction.
  This prevents lost updates and double-spend races without pessimistic locks. (Legacy documents with
  no `Version` field are transparently treated as version 0.)
- **Idempotency.** The client `Idempotency-Key` is the `_id` of a `TransferIdempotency` document — a
  free unique index. A completed key **replays** the prior result; a concurrent duplicate hits a
  duplicate-key error and also replays, so a retried request executes **at most once**.
- **Resilience.** Transient transaction errors and unknown-commit-results are retried using the
  driver's error labels (`TransientTransactionError`, `UnknownTransactionCommitResult`).
- **Risk gate.** Each transfer is scored by the fraud/AML model (`IRiskScoringService`) before commit;
  a `Block` decision aborts the transaction and returns `422` with the reason codes, and the decision
  is written to the `MlDecisions` audit trail.

## Request flow

1. Validate the request (`TransferValidator` — positive amount, ≤ 2 dp, distinct accounts).
2. Fast idempotency replay if the key is already completed.
3. Open a session + transaction; read both accounts under snapshot isolation.
4. Risk-score; block if required.
5. Overdraft check, then version-checked debit and credit.
6. Insert the two ledger transactions, the `FundTransfer` audit, and the idempotency record.
7. Commit (with commit-retry); on any failure the whole transaction aborts.

## API

`POST /api/Transfers` (requires authentication). Header: `Idempotency-Key: <uuid>`.

| Outcome | Status |
| --- | --- |
| Created | `201` |
| Idempotent replay | `200` |
| Validation error | `400` |
| Account not found | `404` |
| Insufficient funds / blocked by risk | `422` |
| Concurrency conflict (retries exhausted) | `409` |

Failures are returned as RFC 7807 `ProblemDetails`; a risk block includes `riskScore`, `riskBand` and
`reasons` extensions.

## Money representation

Transfer amounts are `decimal` and persisted as **Decimal128** — money is never a binary float.
**Known tech-debt:** the legacy `Account.Balance` and `Transaction.Amount` fields are still `double`;
arithmetic here is performed in `decimal` and converted only at that boundary. Production should migrate
those fields to `Decimal128` (a schema/migration change deliberately scoped out of this module).

## Configuration (`appsettings.json` → `Transfers`)

`MaxConcurrencyRetries`, `AllowOverdraft`, `RiskCheckEnabled`, `TransfersCollection`,
`IdempotencyCollection`.

## Requirements

Multi-document transactions require a **replica set** (MongoDB Atlas provides one by default).

## Files

`Models/` (TransferRequest, FundTransfer, TransferResult, TransferOptions, TransferIdempotencyRecord),
`Abstractions/IFundTransferService`, `FundTransferService`, `TransferValidator`, `TransferExceptions`,
`TransfersServiceCollectionExtensions`, and `Controllers/TransferController`.
