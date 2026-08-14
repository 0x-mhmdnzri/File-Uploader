# 📋 BACKLOG — File Uploader Adapter

> این پروژه یک **آداپتر آپلود فایل** در معماری هگزاگونال است.
> فقط مسئولیت **دریافت، ذخیره، resume و تکمیل فایل** را دارد.

---

## 🧭 محدوده مسئولیت

### ✅ داخل این آداپتر
- Chunked / resumable upload
- Storage پشت `IFileStorage`
- Session lifecycle + orphan cleanup
- Checksum اختیاری
- محدودیت‌های پایه (حجم، extension، pending per IP)
- Health / metrics سبک
- **پورت خروجی رویداد** (`IUploadEventPublisher`) — بدون مالکیت منطق downstream

### ❌ خارج از محدوده
- Auth / JWT / Identity
- Rate limit سطح Gateway
- Virus scan / indexing (مصرف‌کننده رویداد)
- CDN / ACL دانلود

---

## ✅ وضعیت فعلی

- [x] Chunked Upload + Parallel Workers + Resume UI
- [x] FileSystem storage + sequential merge
- [x] پورت‌ها: `IFileStorage`, `IUploadRepository`, `IUploadService`, `IUploadEventPublisher`
- [x] Pending → Completed / Expired / Aborted / Failed
- [x] Orphan Cleanup
- [x] Checksum SHA-256 + محدودیت‌های امنیتی پایه
- [x] Serilog + `/health` + `/api/metrics`
- [x] **رویدادهای Complete / Abort / Failed** (آداپتر پیش‌فرض: Logging)

---

## 🎯 تصمیمات معماری

| موضوع | تصمیم |
|-------|--------|
| نقش | آداپتر آپلود |
| پروتکل | HTTP/2 + Chunked REST |
| Storage | FileSystem + `IFileStorage` |
| رویداد خروجی | `IUploadEventPublisher` (جایگزین‌پذیر با bus) |
| Auth | خارج از محدوده |

---

## 🚀 Backlog باقی‌مانده (اختیاری / آینده)

- [ ] آداپتر bus واقعی برای `IUploadEventPublisher` (Rabbit/Kafka/…) — در composition root میزبان
- [ ] آداپتر storage جایگزین (S3-compatible) پشت همان `IFileStorage`
- [ ] GPU-accelerated hashing — فقط اگر bottleneck واقعی شد
- [ ] Brotli / Deflate per-chunk — فقط اگر bandwidth bottleneck شد
- [ ] OpenTelemetry exporter — اگر پلتفرم observability مشترک دارید

### ⛔ عمداً انجام نمی‌شود
- JWT / User management
- Virus engine داخل همین process
- Rate limiting سطح پلتفرم

---

## 📌 پورت رویداد (Outbound)

| رویداد | زمان |
|--------|------|
| `UploadCompletedEvent` | بعد از merge + mark Completed |
| `UploadAbortedEvent` | بعد از abort |
| `UploadFailedEvent` | merge/checksum fail |

آداپتر پیش‌فرض: `LoggingUploadEventPublisher`  
برای fan-out: `CompositeUploadEventPublisher`

شکست publish **نباید** آپلود را fail کند.

---

*آخرین به‌روزرسانی: پورت خروجی رویدادهای lifecycle — محدوده هگزاگونال تکمیل‌تر شد.*
