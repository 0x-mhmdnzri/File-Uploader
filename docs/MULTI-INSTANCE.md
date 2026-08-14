# Multi-instance (P4)

## D1–D4 — Shared metadata & CAS complete (done in code)

### Problem

Two API processes behind a load balancer must not both merge the same upload. In-memory caches are per process and are **hints only**.

### What we ship

| Item | Behavior |
|------|----------|
| `UploadStatus.Completing` | Exclusive merge lease |
| `UploadSession.Version` | Concurrency token on updates |
| `TryBeginCompleteAsync` | SQL/in-memory CAS: `Pending → Completing` (one winner) |
| `TryFinishCompleteAsync` | `Completing → Completed` |
| `TryFailCompleteAsync` | `Completing → Failed` |
| `CompleteAsync` | Drops session cache, reloads DB, CAS, verifies **storage**, then merge |

### Database

```json
"Database": { "Provider": "Sqlite" }
```

Multi-node metadata:

```json
"Database": { "Provider": "Postgres" },
"ConnectionStrings": {
  "Default": "Host=db;Port=5432;Database=fileuploader;Username=app;Password=secret"
}
```

Sqlite remains for single-node lab. For multi-node, use **Postgres** (or one SQLite file on a shared volume with clear locking limits — not recommended).

### Still required for full multi-node (D5+)

Shared **part store** (same `TempPath`/`FinalPath` on a shared volume, or your own blob nodes). Metadata CAS alone does not move bytes between local disks.

### Ops note

Existing Sqlite DB files created before `Version` / `Completing` may need delete + recreate in lab (`EnsureCreated` does not migrate). Prefer migrate or wipe lab DB after pull.
