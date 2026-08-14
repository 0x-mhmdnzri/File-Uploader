 
# 🚀 High-Performance Chunked File Upload Service

A blazing-fast, production-ready file upload service optimized for **very large files (up to 20GB+)**.  
Built with **clean hexagonal architecture**, **resumable chunked uploads**, **parallel processing**, and **Brotli compression on the client** for maximum speed and bandwidth savings.

---

## 🔥 Why This Service?

| Feature                  | Old System     | New System                  |
|--------------------------|----------------|-----------------------------|
| Chunk Size               | 2MB            | **16MB (8x larger)**        |
| Upload Strategy           | Single-thread  | **4–6 parallel workers**    |
| Backend Merge             | Full buffering | **Zero-copy streaming**     |
| Compression               | None           | **Brotli level 11 (client)**|
| Resume Support            | Partial        | **Full with real status**   |
| Throughput                | Slow           | **4x – 10x faster**         |

---

## 🧩 Architecture

### 1. Client (Browser – JavaScript)
- Full resumable uploads with **pause/resume/cancel**
- Real-time speed indicator (MB/s)
- Web Crypto SHA-256 checksum validation
- **Brotli compression (level 11)** – sends 50-70% smaller payloads
- LocalStorage resume after page refresh
- Zero memory spikes

### 2. Backend (.NET 9)
- Hexagonal architecture (ports + adapters)
- **IFileStorage** interface – easy to swap (FileSystem / S3 / Azure / …)
- **Channel-based event bus** with webhook support
- Background orphan cleanup + TTL
- EF Core + SQLite for upload sessions
- Serilog + health checks + metrics

---

## ⚡ Key Features

- ✅ Upload files **up to 20GB+**
- ✅ Full resume support (even after browser close)
- ✅ Parallel chunk uploads (4–6 concurrent)
- ✅ Brotli-compressed chunks on client (maximum speed)
- ✅ SHA-256 checksum validation (client + server)
- ✅ Auto orphan file cleanup with lifecycle rules
- ✅ Clean architecture – easy to extend
- ✅ Production-ready with observability

---

## 📦 API Endpoints

- `POST /api/uploads/initiate` — Start upload  
- `PUT /api/uploads/{id}/chunk/{index}` — Upload compressed chunk  
- `GET /api/uploads/{id}/status` — Check progress  
- `POST /api/uploads/{id}/complete` — Merge & finalize  
- `GET /health` — Server health check  
- `GET /api/metrics` — Live metrics

---

## 🧪 Quick Start

```bash
git clone https://github.com/0x-mhmdnzri/File-Uploader.git
cd File-Uploader
dotnet run
```

Open browser → go to `https://localhost:5000`  
Upload any file >5GB and feel the magic!

---

## 🔮 Next Steps (Optional)

- GPU-accelerated hashing (when CPU becomes bottleneck)
- Real distributed multi-node support
- S3 / Azure Blob / MinIO adapters
- Virus scanning via events

---

**Made with ❤️ by Mohammad Nazari**  
Backend Developer | .NET | Hexagonal Architecture | High-Performance Systems