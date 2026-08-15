# P4.5 — Proof tests

Evidence that multi-instance coordination works without sticky LB.

## Quick matrix

| ID | Proof | How |
|----|-------|-----|
| **D16** | Happy path | `dotnet test` + `tools/proofs/http-proofs.sh` |
| **D17** | Double complete | Parallel CAS unit test + parallel HTTP complete |
| **D18** | Chaos / cleanup | Parallel claim-expired / abort unit tests |
| **CI** | Two real API processes | `docker-compose.multi.yml` + GitHub Actions |

---

## 1. In-process CAS proofs (no server required)

```bash
dotnet test tests/WebApi.Tests/WebApi.Tests.csproj -v n
```

---

## 2. HTTP proofs (single process)

```bash
dotnet run --project WebApi
chmod +x tools/proofs/http-proofs.sh
BASE=http://localhost:5073 ./tools/proofs/http-proofs.sh
```

---

## 3. Two-process proof (shared volume + Postgres)

```bash
docker compose -f docker-compose.multi.yml up --build -d

# wait until both ready
curl -sf http://localhost:5073/health/ready | jq .
curl -sf http://localhost:5075/health/ready | jq .

BASE_A=http://localhost:5073 BASE_B=http://localhost:5075 ./tools/proofs/http-proofs.sh

docker compose -f docker-compose.multi.yml down -v
```

Both `api-a` and `api-b` mount the same `shared_data` volume and the same Postgres — no sticky LB required.

CI runs this path on every push to `dev`/`main` (`.github/workflows/proofs.yml`).

---

## 4. Manual multi-node chaos checklist

| Step | Action | Expect |
|------|--------|--------|
| 1 | Initiate on A | `uploadId` |
| 2 | PUT odd chunks on A, even on B | both 200 |
| 3 | `GET status` on A and on B | same `received` set |
| 4 | `POST complete` on A and B at once | one merge winner; status `Completed` |
| 5 | Re-PUT an existing chunk | `idempotent: true` |
| 6 | Break storage on A | `/health/ready` → 503 on A |
| 7 | Session past TTL | only one node claims cleanup |

---

## Pass criteria

- `dotnet test` — all green
- `http-proofs.sh` — `FAIL=0` (single or dual base)
- CI `multi-node-http` job green

See also: [MULTI-INSTANCE.md](./MULTI-INSTANCE.md), [CLIENT-CONTRACT.md](./CLIENT-CONTRACT.md), [OWNED-BLOB-NODES.md](./OWNED-BLOB-NODES.md).
