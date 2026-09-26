# Performance & Latency Roadmap

> Last updated: 2026-09-26  
> Status: **Open** — work items ready to be picked one-by-one  
> Related: [BENCH.md](BENCH.md), [MULTI-INSTANCE.md](MULTI-INSTANCE.md), [OWNED-BLOB-NODES.md](OWNED-BLOB-NODES.md), [BACKLOG.md](../BACKLOG.md)

This document captures the prioritized list of concrete improvements to reduce end-to-end latency and increase throughput for large (multi-GB) uploads.

Items are ordered by **expected impact × ease of implementation**.  
Check boxes as work lands. Open a PR (or a branch) per item when possible so we can measure before/after with StorageBench + real complete timings.

---

## Current baseline (from README + BENCH)

| File size | Transfer | Complete (merge + SHA-256) | Notes |
|-----------|----------|----------------------------|-------|
| ~1 GB     | ~25–40 s | dominated by disk + hash   | |
| ~7 GB     | ~6–10 min| dominated by merge+hash IO | |
| 12 GB+    | stable   | same                       | |

Hot paths today:
- Chunk PUT → `FileSystemStorage.SaveChunkAsync` (gated by `SemaphoreSlim`)
- Complete → CAS → `VerifyChunksParallelAsync` → Merge (single-pass or parallel) + full SHA-256
- Client: 2–6 adaptive workers, 16 MB chunks

Key knobs already present in `StorageOptions`:
- `MaxConcurrentDiskIo` (default 8)
- `MergeParallelism` (default 4)
- `SinglePassMergeAndHash` (default true)
- `AlwaysComputeFullChecksum` (default true)
- `Hasher` = `"Hardware"`

---

## 1. Server Complete / Merge / Hash path (highest impact on perceived latency)

- [x] **1.1 Config tuning of IO limits**  
  Raise `MaxConcurrentDiskIo` and `MergeParallelism` according to real hardware (NVMe local vs NFS).  
  Re-run StorageBench and document winners in `docs/BENCH.md`.  
  *Effort: low · Risk: low*

- [x] **1.2 Larger IO & hash buffers**  
  Move `BufferSize` in `FileSystemStorage` from 1 MB → 4–8 MB.  
  Keep 4 MB (or larger) in both `HardwareSha256FileHasher` and `Sha256FileHasher`.  
  *Effort: low · Risk: low (watch memory under high concurrency)*

- [x] **1.3 Memory-mapped merge**  
  For the parallel merge path, consider `MemoryMappedFile` + parallel writes instead of many small `FileStream`s.  
  Especially useful when `SinglePassMergeAndHash = false`.  
  *Effort: medium · Risk: medium (platform differences)*

- [x] **1.4 Async / background Complete** — CAS + enqueue on request path; verify/merge/hash runs in CompleteBackgroundService; controller returns 202; client polls
  Make `POST /complete` only acquire the CAS lease and enqueue a background job (Channel / `IHostedService`).  
  Return `202 Accepted` + status URL immediately. Client polls or receives webhook.  
  Biggest UX win for multi-GB files.  
  *Effort: medium–high · Risk: medium (need clear state machine for Completing → Completed/Failed)*

- [ ] **1.5 Optional skip / lazy full SHA-256**  
  Keep `AlwaysComputeFullChecksum = true` as production default.  
  Add a documented lab-only escape hatch + optional Merkle-tree / part-hash verification for trusted environments.  
  *Effort: low · Risk: high if misused in production*

---

## 2. Chunk PUT path (hottest path during active upload)

- [x] **2.1 PipeReader / zero-copy body**  
  Consume `HttpRequest.BodyReader` directly instead of a plain `Stream` in the controller → storage layer.  
  *Effort: medium · Risk: low–medium*

- [x] **2.2 Larger default / adaptive chunk size**  
  Allow client + server to negotiate or config 32–64 MB chunks on high-bandwidth links.  
  Update `MaxChunkSizeBytes` and client defaults accordingly.  
  *Effort: low · Risk: low*

- [x] **2.3 Stronger part-existence cache**  
  Improve `IReceivedChunkCache` hit-rate / TTL; consider a short-lived `ConcurrentDictionary` with sliding expiration.  
  *Effort: low · Risk: low*

- [x] **2.4 Pre-create part directory on Initiate**  
  Move `Directory.CreateDirectory(PartDir)` out of the hot PUT path.  
  *Effort: trivial · Risk: none*

---

## 3. Client (`WebApp/wwwroot/js/upload.js`)

- [x] **3.1 Higher adaptive worker ceiling**  
  Raise max workers from 6 → 12–16 and improve the throughput/RTT adaptive logic.  
  *Effort: low–medium · Risk: low (server must tolerate the extra concurrency)*

- [ ] **3.2 Better HTTP/2 / connection usage**  
  Ensure the browser can fully utilise HTTP/2 multiplexing (or controlled multiple connections).  
  *Effort: medium · Risk: low*

- [ ] **3.3 Adaptive per-chunk compression**  
  Only enable gzip/br when CPU is free and measured bandwidth is the bottleneck.  
  *Effort: low · Risk: low*

- [x] **3.4 Clearer progress for server-side merge**  
  After `complete` returns 202 / “Completing”, show a distinct “Merging & verifying…” state.  
  *Effort: low · Risk: none*

---

## 4. Multi-instance & storage backend (largest production bottleneck)

- [ ] **4.1 Implement Owned Blob Nodes**  
  Follow the design in `docs/OWNED-BLOB-NODES.md`.  
  Move from shared filesystem (NFS/EFS latency) to first-party blob nodes with local SSD.  
  *Effort: high · Risk: high (new component)*

- [ ] **4.2 Local NVMe + async replication**  
  Intermediate step or complement to 4.1.  
  *Effort: high · Risk: high*

- [ ] **4.3 Postgres tuning for CAS hot path**  
  Connection pool size, prepared statements, indexes on `(Status, Version)`, `ExecuteUpdateAsync` cost.  
  *Effort: low–medium · Risk: low*

- [ ] **4.4 Optional Redis for SessionCache / ReceivedChunkCache**  
  Only when multi-instance is enabled and in-memory cache becomes a problem.  
  *Effort: medium · Risk: medium*

---

## 5. Infrastructure & observability

- [ ] **5.1 Kestrel tuning**  
  Explicit limits, KeepAlive, HTTP/2 settings, `MaxRequestBodySize`.  
  *Effort: low · Risk: low*

- [ ] **5.2 Richer metrics + expand StorageBench**  
  Separate histograms for PUT latency, Verify, Merge, Hash.  
  Record p50 / p95 in `docs/BENCH.md`.  
  *Effort: low · Risk: none*

- [ ] **5.3 Reduce logging cost on hot path**  
  Ensure Serilog is asynchronous / sampling on the PUT and complete paths.  
  *Effort: low · Risk: low*

- [ ] **5.4 Advanced Linux IO (io_uring etc.)**  
  Only after the above items are exhausted and we are still IO-bound on local NVMe.  
  *Effort: high · Risk: high*

---

## Suggested first three PRs

1. ~~**Config + buffer tuning** (1.1 + 1.2 + 2.4)~~ — **done** (2026-09-26).
2. **Client worker improvements** (3.1 + 3.4).
3. **Async Complete** (1.4) — biggest perceived latency win.

After each PR, update the measured results table in `docs/BENCH.md`.

---

## How to work on an item

```bash
git checkout -b perf/<short-name>
# implement + tests
dotnet test
# run StorageBench before / after
dotnet run -c Release --project tools/StorageBench -- --size-mb 1024 --chunk-mb 16 --parallelism 8 --rounds 3
git commit -m "perf: <what and why>"
```

Link the PR back to the checkbox in this file.
