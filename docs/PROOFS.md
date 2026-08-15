# P4.5 — Proof tests

Evidence that multi-instance coordination works without sticky LB.

## Quick matrix

| ID | Proof | How |
|----|-------|-----|
| **D16** | Happy path | `dotnet test` + `tools/proofs/http-proofs.sh` |
| **D17** | Double complete | Parallel `TryBeginCompleteAsync` unit test + parallel HTTP complete |
| **D18** | Chaos / cleanup | Parallel `TryClaimExpiredAsync` / `TryAbortAsync` unit tests |

---

## 1. In-process CAS proofs (no server required)

```bash
dotnet test tests/WebApi.Tests/WebApi.Tests.csproj -v n
```

These use `InMemoryUploadRepository` with the same CAS contracts as `EfUploadRepository`:

- Only **one** of N parallel `TryBeginCompleteAsync` wins
- Only **one** `TryFinishCompleteAsync` wins
- Only **one** `TryAbortAsync` / `TryClaimExpiredAsync` wins
- Expired **Completing** sessions are claimable (stuck merger cleanup)

---

## 2. HTTP proofs (API must be running)

```bash
# terminal 1
dotnet run --project WebApi

# terminal 2
chmod +x tools/proofs/http-proofs.sh
BASE=http://localhost:5073 ./tools/proofs/http-proofs.sh
```

Optional two bases (true multi-node):

```bash
BASE_A=http://node-a:5073 BASE_B=http://node-b:5073 ./tools/proofs/http-proofs.sh
```

Covers:

1. Initiate → PUT chunks → complete → `status=Completed`
2. Second PUT same chunk → `idempotent: true`
3. Parallel complete on A and B → final status `Completed`, ≥1 HTTP 200
4. `/health/live` and `/health/ready` Healthy

Requires `curl` + `jq`.

---

## 3. Manual multi-node chaos checklist

Prerequisites: Postgres, shared volume, `MultiInstance:Enabled=true`, two API processes, LB **without** affinity (or alternate `BASE_A` / `BASE_B` by hand).

| Step | Action | Expect |
|------|--------|--------|
| 1 | Initiate on A | `uploadId` |
| 2 | PUT odd chunks on A, even on B | both 200 |
| 3 | `GET status` on A and on B | same `received` set |
| 4 | `POST complete` on A and B at once | one merge winner; status `Completed` |
| 5 | Re-PUT an existing chunk | `idempotent: true` |
| 6 | Stop readiness on A (unmount temp or kill DB path) | LB stops sending to A (`/health/ready` 503) |
| 7 | Leave a session past TTL | only one node logs cleanup claim |

---

## Pass criteria

- `dotnet test` — all green
- `http-proofs.sh` — `FAIL=0`
- Manual multi-node (if available) — table above holds without sticky sessions

See also: [MULTI-INSTANCE.md](./MULTI-INSTANCE.md), [CLIENT-CONTRACT.md](./CLIENT-CONTRACT.md).
