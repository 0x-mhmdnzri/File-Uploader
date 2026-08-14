# Multi-instance (P4)

## P4.0 — Explicit non-goals (enforced at startup when MultiInstance is on)

This product is an **S3-inspired** file service. It does **not** treat external object stores or in-process locks as the multi-node design.

| ID | Non-goal | Why |
|----|----------|-----|
| **NG1** | Fix multi-node with `SemaphoreSlim` / `Mutex` / `ConcurrentDictionary` alone | Those primitives stop at process (or single OS) boundary. They do not order work across machines. |
| **NG2** | Sticky load balancer as long-term HA | Affinity hides the bug until node death, deploy, or uneven routing. Resume and complete must work on **any** healthy instance. |
| **NG3** | External AWS/MinIO as the *product* data plane | Optional adapter may exist for experiments. Product plane is **own** shared part storage (shared volume now; owned blob nodes later). |

### Config gate

```json
"MultiInstance": {
  "Enabled": false,
  "SharedPartStoreConfigured": false,
  "RequirePostgres": true,
  "ForbidExternalObjectStoreAsProductPlane": true
}
```

When `Enabled: true`, process **refuses to start** unless:

1. `Database:Provider` is Postgres (shared metadata — D1), and  
2. `SharedPartStoreConfigured: true` (you affirm every node sees the same temp/final volume — D6), and  
3. `StorageOptions:Provider` is not `S3` (unless you deliberately set `ForbidExternalObjectStoreAsProductPlane: false` for lab).

Single-node lab: leave `Enabled: false`.

Boot always logs the three non-goals so operators cannot claim they were implicit.

---

## D1–D4 — Shared metadata & CAS complete (done)

| Item | Behavior |
|------|----------|
| `UploadStatus.Completing` | Exclusive merge lease |
| `UploadSession.Version` | Concurrency token |
| `TryBeginCompleteAsync` | CAS `Pending → Completing` |
| `TryFinishCompleteAsync` / `TryFailCompleteAsync` | Terminal states |
| `CompleteAsync` | No session-cache trust; DB + storage verify |

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

### Still required for full multi-node (D5+)

Shared **part store** on every API node. Metadata CAS does not move bytes between local disks.

### Ops note

Lab SQLite DBs from before `Version` / `Completing` may need wipe; `EnsureCreated` does not migrate.
