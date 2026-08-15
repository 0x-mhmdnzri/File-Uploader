# BACKLOG — File Uploader (own file service, S3-inspired)

Last updated: 2026-08-15 (`dev`) — **P4 complete** + **optional items complete**.

---

## Done

### Single-node / in-process
- [x] Chunked parallel upload, merge modes, integrity, auth, audit, quotas, StorageBench

### P4.0 Explicit non-goals
- [x] NG1 — Documented + boot log: in-process locks/caches are not cross-node coordination
- [x] NG2 — Sticky LB rejected as HA; `SharedPartStoreConfigured` required when MultiInstance on
- [x] NG3 — External S3/MinIO forbidden as product plane under MultiInstance (override only for lab)
- [x] `MultiInstance` options + `MultiInstanceStartupGuard` fail-fast policy

### P4.1 Shared metadata (D1–D4)
- [x] D1 Postgres/Sqlite provider switch
- [x] D2 Version token
- [x] D3 CAS Pending → Completing → Completed/Failed
- [x] D4 Complete ignores session cache

### P4.2 Shared part store (D5–D7, D9)
- [x] D5 Stable part key `{uploadId}/part/{index}` (FS + S3 aligned)
- [x] D6 Shared filesystem volume gate + boot log of resolved TempPath/FinalPath
- [x] D7 Shared-FS failure modes documented in `docs/MULTI-INSTANCE.md`
- [x] D9 Merge never assumes node-local-only temp (centralized `PartPath` helpers)
- [x] D8 Owned blob nodes **design** (`docs/OWNED-BLOB-NODES.md`)

### P4.3 Thin distributed coordination (D10–D12)
- [x] D10 Idempotent chunk PUT
- [x] D11 CAS abort
- [x] D12 Cluster-safe orphan cleanup

### P4.4 Load balancer & client contract (D13–D15)
- [x] D13 LB policy documented
- [x] D14 `/health/live` + `/health/ready`
- [x] D15 Client contract + PROXY docs

### P4.5 Proof tests (D16–D18)
- [x] D16–D18 unit + HTTP proofs
- [x] Two-process docker compose + GitHub Actions CI

### Optional (completed)
- [x] EF migrations instead of EnsureCreated (`docs/MIGRATIONS.md`)
- [x] Expand HTTP proofs to true two-process CI (`.github/workflows/proofs.yml`)
- [x] D8 owned blob nodes design

---

## Future (not scheduled)

| Item | Notes |
|------|--------|
| Blob node **implementation** | Follow `docs/OWNED-BLOB-NODES.md` when shared FS is no longer enough |
| Provider-split migrations | Only if Sqlite/Postgres SQL diverges heavily |

Primary docs: `docs/MULTI-INSTANCE.md`, `docs/PROOFS.md`, `docs/OWNED-BLOB-NODES.md`, `docs/MIGRATIONS.md`.
