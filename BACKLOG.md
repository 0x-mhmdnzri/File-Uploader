# BACKLOG — File Uploader (own file service, S3-inspired)

Last updated: 2026-08-15 (`dev`)

Goal: multi-instance behind a load balancer without relying on external S3 as the product storage. External S3 adapter stays optional inspiration only; the service itself must remain correct when API nodes do not share process memory or local disk.

Local .NET primitives (`SemaphoreSlim`, `Mutex`, `ConcurrentDictionary`) apply **inside one process only**. Cross-node work is a separate track below.

---

## Done (single-node / in-process)

- [x] Chunked parallel upload, merge modes, integrity, auth, audit, quotas, StorageBench
- [x] In-process concurrency: ConcurrentDictionary, ConcurrentBag, SemaphoreSlim, ArrayPool/Span, Interlocked, Parallel.ForEachAsync
- [x] Optional external object adapter, RabbitMQ, proxy docs, config reference

---

## P4 — Multi-instance (distributed). Do next

Principle: **API stateless + shared metadata + shared (or addressable) part store**. Not “distributed Mutex on every PUT”.

### P4.0 — Explicit non-goals

| ID | Non-goal |
|----|----------|
| NG1 | Solving multi-node with `SemaphoreSlim` / `Mutex` / in-memory cache alone |
| NG2 | Sticky LB as the long-term HA design |
| NG3 | Calling external AWS/MinIO as the *product* storage plane (adapter may remain for experiments) |

### P4.1 — Shared metadata (sessions as source of truth across nodes)

| ID | Task | Acceptance |
|----|------|------------|
| D1 | Replace single-file SQLite-on-one-node assumption with a **shared** metadata store (Postgres preferred; or SQLite only on shared volume with documented limits) | Two API processes can Initiate / GetStatus / Complete the same logical DB |
| D2 | Session row holds: status, totalSize, chunkSize, checksum, clientIp, version/rowversion | Concurrent Complete cannot double-merge |
| D3 | Complete uses **compare-and-swap**: `Pending → Completing → Completed` (or failed) via conditional update | Second Complete is no-op or 409; only one merge wins |
| D4 | Treat in-memory session/chunk caches as **hints only**; always re-check DB + storage on Complete | Kill one node mid-upload; other node still consistent |

### P4.2 — Shared part / object store (own data plane)

| ID | Task | Acceptance |
|----|------|------------|
| D5 | Define part key layout: `{uploadId}/part/{index}` (stable, node-independent) | Any API node reads/writes same part id |
| D6 | Implement **shared filesystem** backend first (NFS/SMB/iSCSI or lab bind-mount) behind `IFileStorage` | Two containers, one volume; all parts visible from both |
| D7 | Document failure modes of shared FS (locks, `FileShare`, stale handles) | Runbook in `docs/MULTI-INSTANCE.md` |
| D8 | Design next step **owned blob nodes** (optional phase): part servers + replication; API only addresses by key | Design doc only until D6 green |
| D9 | Merge/read path never assumes `Path.Combine(localTemp, ...)` is on the same machine as the caller | Complete on node B after PUTs on node A succeeds |

### P4.3 — Coordination (thin distributed locking)

| ID | Task | Acceptance |
|----|------|------------|
| D10 | **No** distributed lock on each chunk PUT; idempotent put by part key | Parallel PUTs same index overwrite safely or reject with clear rule |
| D11 | Distributed lease **only** around merge/complete (DB CAS preferred; else Redis/etcd lease) | Two Completes racing → one merge |
| D12 | Orphan cleanup is cluster-safe: only deletes parts for expired sessions after metadata says so | Cleanup on node A does not delete live upload on node B |

### P4.4 — Load balancer & client contract

| ID | Task | Acceptance |
|----|------|------------|
| D13 | Document LB: round-robin OK when D1+D6 hold; sticky optional optimization only | `docs/MULTI-INSTANCE.md` |
| D14 | Client unchanged: same `uploadId` may hit any instance | Integration test with two ports |
| D15 | Health/readiness: fail ready if metadata or part store unreachable | LB drains bad node |

### P4.5 — Proof tests

| ID | Task | Acceptance |
|----|------|------------|
| D16 | Chaos lab: 2 API processes, **separate** local disks, no shared store → expect missing parts / failed complete | Documents why shared store is mandatory |
| D17 | Happy lab: 2 API processes, shared metadata + shared part volume → full 1GB (or smaller) upload PASS | Primary gate for P4 |
| D18 | Race: two Completes parallel → single final object, one winner | No double file / no corrupt merge |

---

## Suggested order of attack

1. **D16** (show the break)  
2. **D1–D4** (shared metadata + CAS complete)  
3. **D5–D6, D9** (shared part store + storage paths)  
4. **D11–D12, D17–D18** (coordination + proof)  
5. **D13–D15** (LB/docs/health)  
6. **D8** only if you outgrow shared FS  

---

## Optional (not P4)

| Item | Note |
|------|------|
| External MinIO/S3 adapter | Experiment only; not the product data plane |
| GPU hasher | Plug-in behind `IFileHasher` |
| Kafka | Same pattern as RabbitMQ |
| Fill BENCH tables | Host-measured numbers |

---

## Quick switches (single-node today)

| Goal | Config |
|------|--------|
| Local disk | `Provider: FileSystem` |
| Hash-bound complete() | `SinglePassMergeAndHash: true` |
| Strong per-chunk integrity | `RequireChunkSha256: true` |
| Lock API | `Auth:Enabled: true` |
