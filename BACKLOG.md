# BACKLOG — File Uploader (own file service, S3-inspired)

Last updated: 2026-08-15 (`dev`) — D1–D4 implemented.

---

## Done

### Single-node / in-process
- [x] Chunked parallel upload, merge modes, integrity, auth, audit, quotas, StorageBench
- [x] In-process concurrency primitives

### P4.1 Shared metadata (D1–D4)
- [x] D1 — `Database:Provider` Sqlite \| Postgres; shared DB connection string for multi-node metadata
- [x] D2 — `Version` concurrency token + session fields for complete pipeline
- [x] D3 — CAS `Pending → Completing → Completed/Failed` (`TryBegin/Finish/FailCompleteAsync`)
- [x] D4 — Complete ignores session cache; DB + storage verify before merge

---

## P4 remaining

### P4.2 Shared part store
| ID | Task |
|----|------|
| D5 | Stable part key layout `{uploadId}/part/{index}` |
| D6 | Shared filesystem volume behind `IFileStorage` (two nodes, one volume) |
| D7 | Document shared-FS failure modes |
| D8 | Design owned blob nodes (later) |
| D9 | Merge path never assumes node-local-only temp |

### P4.3 Coordination extras
| ID | Task |
|----|------|
| D10 | Idempotent part PUT rules |
| D11 | (CAS already covers complete; Redis lease only if DB CAS insufficient) |
| D12 | Cluster-safe orphan cleanup |

### P4.4–P4.5 LB & proof
| ID | Task |
|----|------|
| D13–D15 | LB docs, client contract, readiness |
| D16 | Chaos: two nodes, no shared store → fail |
| D17 | Happy: shared metadata + shared parts → PASS |
| D18 | Double complete race → one winner |

**Next:** D5–D6 (shared part volume) then D17/D18.

See `docs/MULTI-INSTANCE.md`.
