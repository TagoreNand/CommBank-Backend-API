# Double-Entry Ledger & Transactional Outbox (`CommBank.Ledger`)

Deepens **pillar 1 (data consistency)** and **pillar 4 (auditability)**.

## Double-entry ledger

Every transfer writes a **balanced journal** of two `LedgerEntry` postings — a **debit** on the source and a
**credit** on the destination — sharing a `JournalId`. The accounting invariant **sum(debits) ==
sum(credits)** is enforced before anything is persisted (`LedgerImbalanceException` otherwise). The entries
are written **inside the transfer's MongoDB transaction**, so the ledger can never diverge from the account
balances — it is the immutable money-movement source of truth.

Read it back: **`GET /api/Ledger/transfers/{transferId}`** returns the journal plus a `balanced` flag.

## Transactional outbox

The transfer also writes an `OutboxMessage` (`transfer.completed`) **in the same transaction**. This closes
the classic dual-write problem: the event is persisted **iff** the transfer commits — no lost events, no
phantom events. `OutboxProcessor` (a `BackgroundService`) polls pending messages every 5s and dispatches
each via `IEventPublisher` (default: structured-log publisher; swap for a Kafka/RabbitMQ adapter without
touching callers), marking each **Processed**, or retrying up to 5 attempts before parking it **Failed**.

## Pieces

| Concern | Type | Collection |
| --- | --- | --- |
| Ledger postings | `LedgerService` / `LedgerEntry` | `LedgerEntries` |
| Outbox write (in-transaction) | `OutboxService` / `OutboxMessage` | `Outbox` |
| Outbox relay | `OutboxProcessor` (hosted) | `Outbox` |
| Event sink (seam) | `IEventPublisher` → `LoggingEventPublisher` | — |

Registered via `AddCommBankLedger()`. The integration test
`Transfer_WritesBalancedLedgerAndOutbox` proves a completed transfer yields two balanced ledger entries and
one pending outbox event against a real MongoDB.
