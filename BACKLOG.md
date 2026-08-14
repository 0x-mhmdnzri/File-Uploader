# BACKLOG — File Uploader (personal file service)

Last updated: 2026-08-14 (`dev`) — P0 measure/harden closed in code + StorageBench tool.

---

## Done

### Core upload
- [x] Chunked upload (16 MB default), resume, pause/cancel
- [x] Client parallel workers (2–6, adaptive)
- [x] `IFileStorage` + `FileSystemStorage`
- [x] Orphan cleanup (`OrphanCleanupService` only)
- [x] Extension / size / IP pending limits
- [x] Serilog, health checks, metrics

### Concurrency and latency
- [x] Singleton received-chunk cache, session cache
- [x] ConcurrentBag + Interlocked verify
- [x] SemaphoreSlim disk gate, ArrayPool/Memory path
- [x] Pre-allocate + parallel offset writes
- [x] Optional single-pass merge + SHA
- [x] No CSV write per chunk; validate before disk write

### P0 measure and harden (2026-08-14)
- [x] R3 — delete obsolete `UploadCleanupService`
- [x] R2/R1 tooling — `tools/StorageBench` stress + integrity compare
- [x] Parallel merge part-length + final-length checks
- [x] Single-pass written-byte check
- [x] CRC mismatch deletes bad part (`DeleteChunkAsync`)
- [x] `docs/BENCH.md` runbook

### Integrity / transport / events
- [x] Full-file SHA-256 on complete
- [x] Optional per-chunk CRC32
- [x] Per-chunk decompression
- [x] Channel event bus + webhook handler

---

## Remaining

### P1 — client and integrity polish

| ID | Task |
|----|------|
| R4 | True constant-memory client SHA (WASM or server-only) |
| R5 | Mid-flight adaptive worker pool |
| R7 | Optional per-chunk SHA-256 header |

### P2 — product / ops

| ID | Task |
|----|------|
| R8 | AuthN/AuthZ on API |
| R9 | Structured audit log |
| R10 | Storage quota beyond pending sessions |
| R11 | Fill README/BENCH tables with measured host numbers |

### P3 — deferred

| ID | Task |
|----|------|
| R12 | S3 / Azure / MinIO |
| R13 | GPU hasher |
| R14 | Rabbit/Kafka handler |
| R15 | HTTP/2 / proxy guide |

---

## Run bench on your machine

```bash
dotnet run -c Release --project tools/StorageBench -- --size-mb 256 --chunk-mb 16 --parallelism 4 --rounds 3
```

See `docs/BENCH.md`.
