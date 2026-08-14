# BACKLOG — File Uploader (personal file service)

Last updated: 2026-08-14 (`dev`) — P1 client/integrity polish closed.

---

## Done

### Core + concurrency + P0 harden
- [x] Chunked parallel upload, resume, orphan cleanup
- [x] Disk IO gate, ArrayPool, parallel/single-pass merge, session cache
- [x] StorageBench, CRC delete on mismatch, part/final length checks
- [x] Obsolete cleanup service removed

### P1 — client and integrity polish (2026-08-14)
- [x] R4 — Constant-memory streaming client SHA-256 (incremental pure JS, 2 MB slices, any file size)
- [x] R5 — Mid-flight adaptive worker pool (`pump` scales active workers toward `adaptiveWorkers`)
- [x] R7 — Optional `X-Chunk-SHA256` + `RequireChunkSha256` (tee + IncrementalHash; delete part on mismatch)

### Also done earlier
- [x] Optional CRC32, decompression, Channel events, webhook

---

## Remaining

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

## Config (integrity)

```json
"StorageOptions": {
  "RequireChunkCrc32": false,
  "RequireChunkSha256": false
}
```

Enable `RequireChunkSha256` when you want crypto-strength per-chunk rejection (higher CPU).

## Bench

```bash
dotnet run -c Release --project tools/StorageBench -- --size-mb 256 --chunk-mb 16 --parallelism 4 --rounds 3
```
