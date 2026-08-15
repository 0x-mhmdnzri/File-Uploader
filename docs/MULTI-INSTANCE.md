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

---

## P4.2 — Shared part / object store (own data plane)

Metadata CAS does **not** move bytes. Every API node must see the **same** part and final objects.

### D5 — Stable part key

Layout on the shared volume (and aligned S3 experimental keys):

```
{TempPath}/{uploadId:N}/part/{index}
```

Examples:

```
temp/a1b2c3d4e5f6.../part/0
temp/a1b2c3d4e5f6.../part/1
uploads/my-video.mp4
```

All path resolution goes through centralized helpers in `FileSystemStorage` (`PartPath`, `PartDir`, `SessionTempDir`). Merge, verify, delete, and list never invent node-local paths (D9).

> **Migration note:** Older layout was `{TempPath}/{uploadId}/{uploadId}.part{n}`. Wipe lab `temp/` after upgrade; in-flight sessions with the old layout will not resume.

### D6 — Shared filesystem volume

1. Provision one volume visible to every API node (NFS, SMB, Gluster, EFS, CephFS, etc.).
2. Point **all** nodes at the same absolute paths:

```json
"StorageOptions": {
  "Provider": "FileSystem",
  "TempPath": "/mnt/uploader/temp",
  "FinalPath": "/mnt/uploader/uploads"
},
"MultiInstance": {
  "Enabled": true,
  "SharedPartStoreConfigured": true
}
```

3. Set `SharedPartStoreConfigured: true` only after you have verified that a file written on node A is readable on node B under those paths.
4. On boot with MultiInstance enabled, the process logs the resolved full paths so you can confirm the mount.

### D7 — Shared-FS failure modes

| Failure | Symptom | Mitigation |
|---------|---------|------------|
| Mount missing / wrong path on one node | Chunk PUT 200 on A, complete on B reports missing parts | Health check + readiness that probes TempPath; never rely on sticky LB |
| Split-brain mounts (two different volumes) | Same key, different bytes | Single volume ID; ops runbook; `SharedPartStoreConfigured` is an explicit ack |
| NFS close-to-open / cache delay | B does not see part just written by A | Prefer mounts with close-to-open consistency; short sleep is **not** a product fix |
| Permission mismatch across nodes | 500 on SaveChunk / Merge | Same UID/GID or ACL on all nodes |
| Disk full on shared volume | Writes fail mid-upload | Quotas + monitoring on the shared volume |
| Partial network partition | Some nodes see volume, others timeout | Fail the node (readiness); clients retry another instance |

**Rule:** Resume and `/complete` must succeed on **any** healthy instance. If that requires sticky routing, the part store is not shared correctly.

### D9 — Merge never assumes node-local-only temp

- Merge reads only via `PartPath(uploadId, index)`.
- Final file is written only under configured `FinalPath`.
- No process-local scratch directory is used as the source of truth for parts (S3 adapter may use a local merge buffer, but product plane under MultiInstance is FileSystem on the shared volume).

### D8 — Owned blob nodes (later)

Future design: dedicated blob nodes with an internal object API. Until then, shared volume + stable keys is the supported multi-node data plane.

---

## Minimal multi-node checklist

1. Postgres for sessions (D1–D4).
2. Shared volume mounted at the same `TempPath` / `FinalPath` on every API node (D6).
3. `MultiInstance:Enabled=true` and `SharedPartStoreConfigured=true`.
4. `StorageOptions:Provider=FileSystem`.
5. Load balancer **without** session affinity requirement (NG2).
6. Wipe old temp folders after the D5 layout change.

### Ops note

Lab SQLite DBs from before `Version` / `Completing` may need wipe; `EnsureCreated` does not migrate.
