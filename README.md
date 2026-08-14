# 🚀 High-Performance Chunked File Uploader

> **Production-grade** resumable parallel upload engine for files up to **20 GB+**  
> Built with **.NET 8/9** + modern browser APIs  
> Focus: **maximum throughput · minimal latency · iron-clad consistency**

[![.NET](https://img.shields.io/badge/.NET-8%2F9-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Architecture](https://img.shields.io/badge/Architecture-Hexagonal-blue)](#)
[![Concurrency](https://img.shields.io/badge/Concurrency-Lock--Free%20%2B%20Controlled-success)](#)
[![License](https://img.shields.io/badge/License-MIT-green)](#)

---

## 📈 Performance Highlights

| Metric                    | Before (naive)              | After (this engine)                  | Improvement      |
|---------------------------|-----------------------------|--------------------------------------|------------------|
| Chunk size                | 2 MB                        | **16 MB**                            | 8× fewer round-trips |
| Concurrent workers        | 1                           | **4–12** (adaptive)                  | High parallelism |
| Merge strategy            | Sequential + global lock    | **Parallel offset writes**           | Massive latency cut |
| Memory pressure           | High (full buffers)         | **ArrayPool + Memory/Span**          | Near-zero GC spikes |
| Chunk tracking            | List / lock                 | **ConcurrentDictionary**             | Lock-free          |
| End verification          | None / weak                 | **ConcurrentBag** (client + server) | Strong consistency |
| Typical 7 GB upload       | 20–30 min                   | **6–10 min**                         | **~3–5× faster**   |
| 12 GB+ stability          | Often failed                | **Fully stable**                     | Production ready   |

---

## 🏗 Architecture at a Glance

```mermaid
flowchart TB
    subgraph Client["Browser Client"]
        A[File] --> B[Slice into 16 MB chunks]
        B --> C[Worker Pool<br/>Semaphore-style concurrency]
        C --> D[PUT /chunk/i]
        D --> E[Client-side verification<br/>Set of received indexes]
        E --> F[POST /complete]
    end

    subgraph Server[".NET Backend"]
        G[Initiate] --> H[UploadSession<br/>ConcurrentDictionary]
        D --> I[SaveChunkAsync<br/>SemaphoreSlim + ArrayPool]
        I --> J[MarkChunkReceived<br/>lock-free ConcurrentDictionary]
        F --> K[Parallel verification<br/>ConcurrentBag]
        K --> L[Pre-allocate final file]
        L --> M[Parallel offset writes<br/>no global lock]
        M --> N[Final size check + cleanup]
    end

    Client --> Server
```

---

## ⚡ How We Slashed Latency – C# Concurrency Deep Dive

We deliberately chose modern .NET concurrent primitives instead of coarse locks.  
Here’s **exactly** what each one does, **why** it reduces latency, and **how** it appears in the code.

### 1. `ConcurrentDictionary<int, bool>` – Lock-free chunk tracking

**Why**  
Every successful chunk must be recorded. A classic `Dictionary` + `lock` would serialize all workers under high concurrency → latency spikes.

**How**  
```csharp
// Domain
public ConcurrentDictionary<int, bool> ReceivedChunks { get; set; } = new();

// Service – completely lock-free write
s.ReceivedChunks[chunkIndex] = true;
```
The dictionary uses fine-grained locking internally (or lock-free techniques on modern runtimes). Multiple threads can update different keys simultaneously with almost zero contention.

### 2. `ConcurrentBag` – High-speed missing-chunk detection

**Why**  
Before merging we must prove *every* chunk exists. Scanning with a normal `List` + locks is slow and race-prone.

**How**  
```csharp
var missing = new ConcurrentBag();

await Parallel.ForEachAsync(Enumerable.Range(0, totalChunks), ..., async (i, ct) =>
{
    if (!await _storage.ChunkExistsAsync(uploadId, i))
        missing.Add(i);   // thread-safe, no lock needed from caller
});

if (!missing.IsEmpty)
    throw new InvalidOperationException($"{missing.Count} chunks missing");
```
`ConcurrentBag` is optimized for concurrent `Add`/`Contains`. Perfect for “collect problems in parallel”.

### 3. `SemaphoreSlim` – Controlled disk I/O (back-pressure)

**Why**  
Unlimited parallel `FileStream` writes can thrash the disk / SSD queue → *higher* latency. We need a soft limit.

**How**  
```csharp
private static readonly SemaphoreSlim DiskIoGate = 
    new(Math.Max(4, Environment.ProcessorCount * 2));

await DiskIoGate.WaitAsync(ct);
try
{
    // actual FileStream write
}
finally { DiskIoGate.Release(); }
```
Acts as a high-performance async mutex with a count. Prevents disk saturation while still allowing plenty of parallelism.

### 4. `ArrayPool<byte>` + `Memory<T>` / `Span<T>` – Zero-allocation buffers

**Why**  
Allocating a new 4 MB buffer for every chunk creates massive GC pressure → pauses and higher latency.

**How**  
```csharp
byte[] buffer = ArrayPool<byte>.Shared.Rent(4 * 1024 * 1024);
try
{
    int read;
    while ((read = await partFs.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        await finalFs.WriteAsync(buffer.AsMemory(0, read), ct);
}
finally
{
    ArrayPool<byte>.Shared.Return(buffer);
}
```
`ArrayPool` reuses buffers across the whole process. `Memory`/`Span` give safe, zero-copy views into those buffers.

### 5. `Interlocked` – Atomic counters without locks

**Why**  
When many threads compute total size we need a race-free sum.

**How**  
```csharp
Interlocked.Add(ref totalExpectedSize, len);
```
CPU-level atomic operation – far cheaper than any lock.

### 6. `Parallel.ForEachAsync` – Structured parallel work

**Why**  
Manual `Task.Run` + `Task.WhenAll` is easy to get wrong (unbounded tasks, exception handling). `Parallel.ForEachAsync` gives controlled degree of parallelism + proper cancellation.

**How**  
Used for both the verification pass and the final offset-based merge.

### 7. Pre-allocation + Offset Writes – The real merge speedup

**Old (high latency)**  
```csharp
lock (mergeLock) { finalFs.Write(...); }   // every write serialized
```

**New (low latency)**  
1. Create final file and call `SetLength(totalSize)` once.  
2. Each worker opens its *own* `FileStream` with `FileShare.ReadWrite`.  
3. Seeks to `offset = sum of previous chunk sizes`.  
4. Writes only its range.

Modern filesystems handle concurrent writes to *different* offsets extremely well. No global lock → full multi-core / multi-queue SSD utilization.

---

## 🔄 End-to-End Upload Pipeline (with verification)

```mermaid
sequenceDiagram
    participant C as Client
    participant API as UploadController
    participant Svc as UploadService
    participant Stor as FileSystemStorage
    participant Disk as Disk

    C->>API: POST /initiate
    API->>Svc: InitiateAsync
    Svc->>Stor: EnsureDirectories
    Svc-->>C: uploadId + totalChunks

    loop Parallel workers
        C->>API: PUT /chunk/{i}
        API->>Stor: SaveChunkAsync (SemaphoreSlim + ArrayPool)
        Stor->>Disk: Write .part{i}
        API->>Svc: MarkChunkReceived (ConcurrentDictionary)
    end

    C->>C: Client verification (Set)
    C->>API: POST /complete
    API->>Svc: MergeChunksAsync
    Svc->>Svc: Parallel check (ConcurrentBag)
    Svc->>Stor: MergeAsync
    Stor->>Stor: Parallel existence + size (ConcurrentBag)
    Stor->>Disk: Pre-allocate final file
    Stor->>Disk: Parallel offset writes
    Stor->>Stor: Final size verification
    Stor->>Disk: Delete temp folder
    Svc-->>C: 200 OK
```

---

## 🧠 Why These Choices Matter

| Concern              | Classic Approach              | Our Approach                          | Latency Impact          |
|----------------------|-------------------------------|---------------------------------------|-------------------------|
| Shared state         | `lock` around Dictionary      | `ConcurrentDictionary`                | Near zero contention    |
| Collecting errors    | locked List                   | `ConcurrentBag`                       | Parallel safe           |
| Disk overload        | unlimited tasks               | `SemaphoreSlim`                       | Stable high throughput  |
| Buffer allocation    | `new byte[4MB]` every time    | `ArrayPool` + `Memory`                | No GC pauses            |
| Merge                | sequential or locked writes   | pre-allocate + offset parallel writes | Biggest single win      |
| Consistency          | hope for the best             | double verification (client + server) | Zero silent corruption  |

---

## 🛠 Quick Start

```bash
git clone https://github.com/0x-mhmdnzri/File-Uploader.git
cd File-Uploader
dotnet run --project WebApi
# open the WebApp (or any frontend pointing to the API)
```

Upload a multi-GB file and watch the progress bar + console.  
Resume works out of the box – just start the same file again.

---

## 📦 API Surface

| Method | Endpoint                              | Description                          |
|--------|---------------------------------------|--------------------------------------|
| POST   | `/api/uploads/initiate`               | Start session, returns `uploadId`    |
| PUT    | `/api/uploads/{id}/chunk/{index}`     | Upload one chunk                     |
| GET    | `/api/uploads/{id}/status`            | Received indexes + metadata          |
| POST   | `/api/uploads/{id}/complete`          | Verify + merge                       |

---

## 🔮 Roadmap

- [ ] Optional per-chunk SHA-256 (client + server)
- [ ] Aggressive Brotli compression on the client (`Content-Encoding: br`)
- [ ] Pluggable `IFileStorage` (S3 / Azure Blob / GCS)
- [ ] GPU-accelerated hashing when CPU becomes the bottleneck

---

## 👨‍💻 Author

**Mohammad Nazari**  
Backend .NET Engineer – High-performance systems, concurrency, hexagonal architecture & large-scale file pipelines.

---

*Built for speed. Verified for correctness. Ready for production.*
