# BACKLOG — File Uploader (personal file service)

Last updated: 2026-08-14 (`dev`) — P3 shipped (adapters + docs).

---

## Done

### Core through P2
- [x] Parallel chunked upload, merge modes, integrity, auth, audit, quotas, StorageBench

### P3 — previously deferred
- [x] R12 — S3/MinIO/R2 via `S3FileStorage` + `StorageOptions:Provider=S3` + `ObjectStorage`
- [x] R13 — `HardwareSha256FileHasher` (OS/hardware crypto path); discrete GPU remains a custom `IFileHasher` plug-in
- [x] R14 — `RabbitMqUploadEventHandler` when `RabbitMq:Enabled=true`
- [x] R15 — `docs/PROXY.md` (HTTP/2, nginx, Caddy)

### Config
- [x] Full knob reference: `docs/CONFIG.md`

---

## Optional follow-ups (not blocking)

| Item | Note |
|------|------|
| True discrete GPU SHA | Vendor SDK behind `IFileHasher` |
| S3 server-side compose / UploadPartCopy | Current merge streams via temp local for portability |
| Kafka handler | Same pattern as RabbitMQ |
| Fill BENCH tables | Run StorageBench on your host |

---

## Quick switches

| Goal | Config |
|------|--------|
| Local disk | `Provider: FileSystem` |
| MinIO | `Provider: S3` + `ObjectStorage:ServiceUrl` |
| Hash-bound complete() | `SinglePassMergeAndHash: true` |
| SSD assemble-bound | `SinglePassMergeAndHash: false` |
| Strong per-chunk integrity | `RequireChunkSha256: true` |
| Lock API | `Auth:Enabled: true` |
| Bus out | `RabbitMq:Enabled: true` |
