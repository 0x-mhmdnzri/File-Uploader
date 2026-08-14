# BACKLOG — File Uploader (personal file service)

Hexagonal upload adapter: chunked parallel upload, disk as source of truth.

Last updated: 2026-08-14 (`dev`)

---

## Done

### Core upload
- [x] Chunked upload (16 MB default), resume, pause/cancel
- [x] Client parallel workers (2–6, adaptive from measured throughput)
- [x] `IFileStorage` + `FileSystemStorage`
- [x] Orphan cleanup (`OrphanCleanupService` only)
- [x] Extension / size / IP pending limits
- [x] Serilog, health checks, in-process metrics

### Concurrency and latency
- [x] Singleton `ConcurrentDictionary` received-chunk cache
- [x] Short-TTL `SessionCache` for hot-path session reads
- [x] `ConcurrentBag` + `Interlocked` parallel verification
- [x] `SemaphoreSlim` global disk IO gate
- [x] `ArrayPool` + `Memory`/`Span` copy path
- [x] Pre-allocate final file + parallel offset writes
- [x] Optional **single-pass merge + SHA** (`SinglePassMergeAndHash`)
- [x] Integrated hash return from `MergeAsync` (no second service-level open)
- [x] No CSV/DB write on every chunk PUT
- [x] Validate session **before** writing chunk to disk

### Integrity and transport
- [x] Full-file SHA-256 verify on complete (optional client checksum)
- [x] Optional per-chunk CRC32 (`X-Chunk-CRC32`, `RequireChunkCrc32`)
- [x] Per-chunk decompression (`Content-Encoding: gzip|deflate|br`)
- [x] `IFileHasher` port (default CPU SHA-256)

### Events
- [x] In-process Channel bus + dispatcher + handlers
- [x] Logging handler + optional webhook handler

### Docs
- [x] README performance deep-dive (Mermaid + primitives)
- [x] This backlog refreshed

---

## Remaining

### P0 — measure and harden (do next)

| ID | Task | Why |
|----|------|-----|
| R1 | Bench 1 GB / multi-GB: initiate, per-chunk, verify, merge, hash | Prove latency wins on your hardware; pick default merge mode |
| R2 | Stress parallel offset writes on target volume (SSD/HDD/NAS) | `FileShare.ReadWrite` behavior is environment-specific |
| R3 | Delete obsolete `UploadCleanupService` stub from repo | Dead code; cleanup is `OrphanCleanupService` only |

### P1 — client and integrity polish

| ID | Task | Why |
|----|------|-----|
| R4 | True constant-memory client SHA (WASM or skip + server-only) | Current path still materializes ≤512 MB for WebCrypto |
| R5 | Mid-flight adaptive worker resize (or continuous pool) | Today adaptation affects worker count between runs/batches |
| R6 | On CRC mismatch, delete bad part before 400 | Avoid leaving corrupt `.part` until overwrite |
| R7 | Optional per-chunk SHA-256 header (stronger than CRC32) | Early reject with crypto-strength check |

### P2 — product / ops

| ID | Task | Why |
|----|------|-----|
| R8 | AuthN/AuthZ on API (API key or JWT) | Personal file service still open on network bind |
| R9 | Structured audit log for initiate/complete/abort | Governance and incident review |
| R10 | Quota per user/IP beyond pending-session count | Storage growth control |
| R11 | Replace illustrative README timings with measured numbers | Credibility |

### P3 — deferred by choice

| ID | Task | Why deferred |
|----|------|----------------|
| R12 | S3 / Azure Blob / MinIO behind `IFileStorage` | Explicitly out of scope for now |
| R13 | GPU `IFileHasher` | Only if SHA is proven bottleneck |
| R14 | Rabbit/Kafka `IUploadEventHandler` | Channel bus is enough in-process |
| R15 | HTTP/2 / reverse-proxy tuning guide | Infra, not app code |

---

## Config knobs (current)

```json
"StorageOptions": {
  "MaxConcurrentDiskIo": 8,
  "MergeParallelism": 4,
  "SinglePassMergeAndHash": false,
  "RequireChunkCrc32": false,
  "SessionCacheTtlSeconds": 30,
  "MaxFileSizeBytes": 21474836480,
  "PendingTtlHours": 24
}
```

- Prefer `SinglePassMergeAndHash: true` if complete() hash time dominates on your disk.
- Prefer parallel merge (default) on fast SSD when assemble time dominates.

---

## Event bus (unchanged)

```
UploadService
  → IUploadEventPublisher (ChannelUploadEventPublisher)
      → Channel (bounded)
          → UploadEventDispatcherService
              → IUploadEventHandler[]
                  ├─ LoggingUploadEventHandler
                  └─ WebhookUploadEventHandler (optional)
```

Webhook:

```json
"Webhook": { "Url": "https://your-hook", "TimeoutSeconds": 10 }
```

---

## Suggested order of attack

1. R1 + R2 (measure; set merge defaults)
2. R6 (CRC failure hygiene)
3. R3 (delete stub)
4. R4 / R8 when you expose beyond localhost
5. R12 only when local disk is no longer enough
