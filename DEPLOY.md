# Running CommBank locally

The whole platform runs with one command. From the repository root:

```bash
docker compose up --build
```

This starts the API, a single-node **MongoDB replica set** (required for multi-document transactions),
Prometheus, Grafana and Jaeger.

## Services

| Service | URL | Notes |
| --- | --- | --- |
| API (Swagger) | http://localhost:8080/swagger | REST API |
| API metrics | http://localhost:8080/metrics | Prometheus exposition |
| API health | http://localhost:8080/health/ready | readiness (incl. MongoDB) |
| Grafana | http://localhost:3000 | anonymous viewer enabled; admin/admin |
| Prometheus | http://localhost:9090 | metrics store |
| Jaeger | http://localhost:16686 | distributed traces |

The **"CommBank API — Overview"** dashboard is auto-provisioned in Grafana (transfer throughput, blocked
transfers, transfer-amount p95, HTTP request rate).

## Smoke test

```bash
# 1. Register a user (open endpoint; server assigns the "Customer" role)
curl -s -X POST http://localhost:8080/api/User \
  -H "Content-Type: application/json" \
  -d '{"name":"Tagore","email":"tagore@example.com","password":"Password123"}'

# 2. Log in -> copy the "token" from the response
curl -s -X POST http://localhost:8080/api/Auth/Login \
  -H "Content-Type: application/json" \
  -d '{"email":"tagore@example.com","password":"Password123"}'

# 3. Call a protected endpoint (transfers are idempotent on the key)
curl -s -X POST http://localhost:8080/api/Transfers \
  -H "Authorization: Bearer <TOKEN>" \
  -H "Idempotency-Key: 11111111-1111-1111-1111-111111111111" \
  -H "Content-Type: application/json" \
  -d '{"sourceAccountId":"<24-hex>","destinationAccountId":"<24-hex>","amount":100.00,"userId":"<24-hex>"}'
```

Generate traffic and watch the Grafana dashboard and Jaeger traces populate.

## Notes

- **Signing key / credential:** `docker-compose.yml` sets a throwaway `Jwt__SigningKey` and an in-network
  Mongo connection string for local demo only. Override both in any real environment (env vars / secrets).
- **Replica set:** the `mongo-init` one-shot service runs `rs.initiate(...)`; the API waits for it to
  complete before starting (it needs the replica set for transactions).
- **Traces:** the API exports OTLP to Jaeger because `OpenTelemetry__OtlpEndpoint=http://jaeger:4317` is
  set in compose; unset it to disable.
- **Metric names:** Prometheus panel queries use the OTel→Prometheus naming
  (`commbank_transfers_completed_total`, `commbank_transfers_amount_bucket`, …). If the exporter sanitises
  a name differently in your version, adjust the dashboard query — the metric definitions live in
  `AppDiagnostics`.

## CI/CD

`.github/workflows/ci.yml` runs on push/PR: restore + build + test, a **gitleaks** secret scan,
**CodeQL** analysis, and a Docker image build (cached). Move `.github/` to your git repository root if the
repo root differs from this folder.
