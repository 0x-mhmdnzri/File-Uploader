# 📋 BACKLOG — File Uploader Adapter

> آداپتر آپلود در معماری هگزاگونال — فقط وظیفه آپلود.

---

## ✅ وضعیت فعلی

- [x] Chunked / resume / parallel / orphan cleanup
- [x] `IFileStorage` + FileSystem
- [x] Checksum + محدودیت‌های پایه
- [x] Serilog / health / metrics
- [x] **Bus واقعی (in-process Channel + dispatcher + handlers)**
- [x] **Webhook handler** (با `Webhook:Url`)
- [x] **پورت `IFileHasher`** (پیش‌فرض CPU SHA-256؛ GPU بعداً قابل جایگزینی)
- [x] **Per-chunk decompression** (`Content-Encoding: gzip|deflate|br`)

### عمداً بعداً

- [ ] Storage جایگزین پشت `IFileStorage` (S3 و …)
- [ ] GPU-accelerated `IFileHasher` — فقط اگر bottleneck واقعی SHA-256 دیده شد

---

## 📌 Event bus

```
UploadService
    → IUploadEventPublisher (ChannelUploadEventPublisher)
        → Channel (bounded 256)
            → UploadEventDispatcherService
                → IUploadEventHandler[]
                    ├─ LoggingUploadEventHandler
                    └─ WebhookUploadEventHandler (اختیاری)
```

فعال‌سازی webhook:

```json
"Webhook": { "Url": "https://your-orchestrator/hooks/upload", "TimeoutSeconds": 10 }
```

برای Rabbit/Kafka: یک `IUploadEventHandler` جدید بنویس؛ به `UploadService` دست نزن.

---

## 📌 Per-chunk compression

کلاینت می‌تواند هر chunk را فشرده بفرستد:

```
PUT /api/uploads/{id}/chunk/{i}
Content-Encoding: br   # یا gzip / deflate
```

روی دیسک همیشه raw ذخیره می‌شود → merge بدون تغییر.

---

## 📌 Hashing

```csharp
builder.Services.AddSingleton<IFileHasher, Sha256FileHasher>();
// بعداً:
// builder.Services.AddSingleton<IFileHasher, GpuSha256FileHasher>();
```

---

*آخرین به‌روزرسانی: Channel bus + webhook + hasher port + chunk decompression.*
