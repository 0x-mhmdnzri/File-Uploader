# BACKLOG — File Uploader (own file service, S3-inspired)

Last updated: 2026-08-15 (`dev`) — P4.0 + D1–D4 done.

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

---

## P4 remaining — next

### P4.2 Shared part store
| ID | Task |
|----|------|
| D5 | Stable part key `{uploadId}/part/{index}` |
| D6 | Shared filesystem volume (two nodes, one volume); set `SharedPartStoreConfigured` |
| D7 | Shared-FS failure modes in docs |
| D8 | Owned blob nodes design (later) |
| D9 | Merge never assumes node-local-only temp |

### P4.3–P4.5
| ID | Task |
|----|------|
| D10–D12 | Idempotent PUT, cluster-safe cleanup |
| D13–D15 | LB docs, readiness |
| D16–D18 | Chaos / happy / double-complete proof |

**Next implement:** D5–D6.

See `docs/MULTI-INSTANCE.md`.
