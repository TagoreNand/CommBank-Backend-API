# Integration tests (Testcontainers)

These run `FundTransferService` against a **real MongoDB replica set** started in a throwaway container
(Testcontainers), so they prove the transactional guarantees end-to-end instead of mocking them.

## Requirements

- **Docker** must be running on the host / CI runner. The `MongoReplicaSetFixture` starts `mongo:6.0` with
  `--replSet rs0`, runs `rs.initiate(...)`, waits for a primary, and connects with `directConnection=true`
  (a single-node replica set is the minimum needed for multi-document transactions).
- The unit tests need **no** Docker; only the `Mongo` collection does.

## What's proven

| Test | Guarantee |
| --- | --- |
| `Transfer_MovesFundsAtomically` | atomic debit + credit, two ledger transactions, one audit record |
| `Transfer_IsIdempotent_OnSameKey` | same Idempotency-Key debits exactly once and replays the result |
| `Transfer_InsufficientFunds_Throws_AndLeavesBalancesUntouched` | rejected, balances untouched, nothing persisted |
| `ConcurrentTransfers_HaveNoLostUpdates` | N parallel transfers leave the **exact** final balance and version — optimistic concurrency prevents lost updates |

## Run

```bash
# with Docker available
dotnet test CommBank-Server-main/Server.sln
```

Risk scoring is disabled in these tests (`RiskCheckEnabled = false`) so they isolate transactional
behaviour; risk gating is covered by the unit tests under `CommBank.Tests/AI`.
