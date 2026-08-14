# 📋 BACKLOG — File Uploader Adapter

> این پروژه یک **آداپتر آپلود فایل** در معماری هگزاگونال است.
> فقط مسئولیت **دریافت، ذخیره، resume و تکمیل فایل** را دارد — نه Identity، نه Gateway، نه بقیه سرویس‌ها.

---

## 🧭 محدوده مسئولیت (Bounded Context)

### ✅ داخل این آداپتر

| حوزه | توضیح |
|------|--------|
| Chunked / resumable upload | initiate, chunk, status, complete, abort |
| Storage | FileSystem (قابل تعویض با `IFileStorage`) |
| Session lifecycle | Pending → Completed / Expired / Aborted / Failed |
| Orphan cleanup | TTL + BackgroundService |
| Integrity | SHA-256 اختیاری |
| محدودیت‌های پایه | حجم فایل، chunk، extension، pending per IP |
| Health / metrics سبک | برای مانیتورینگ خود آداپتر |

### ❌ خارج از این آداپتر (مالکیت سرویس/لایه دیگر)

| موضوع | کجا باید باشد |
|--------|----------------|
| Authentication / JWT / User identity | Identity service یا API Gateway |
| Authorization سطح دامنه (چه کسی به چه فایلی دسترسی دارد) | سرویس دامنه / Policy |
| Rate limiting سراسری / WAF | API Gateway |
| Virus scanning | آداپتر/سرویس جدا (رویداد بعد از complete) |
| CDN / serving فایل نهایی | سرویس دانلود یا object edge |
| Distributed multi-node coordination | فقط اگر واقعاً چند نود آپلود داشته باشیم |

**نتیجه:** Auth را در این ریپو پیاده نمی‌کنیم مگر به‌صورت اختیاری و نازک (مثلاً API Key از Gateway) — و آن هم فقط اگر صریحاً لازم شود.

---

## ✅ وضعیت فعلی (داخل محدوده)

- [x] Chunked Upload + Parallel Workers
- [x] Resume (status + localStorage + UI دمو)
- [x] FileSystem storage + sequential merge
- [x] لایه پورت/آداپتر تمیز (`IFileStorage`, `IUploadRepository`, `IUploadService`)
- [x] EF Core + SQLite برای session
- [x] Pending → Completed / Expired / Aborted / Failed
- [x] Orphan Cleanup BackgroundService
- [x] Abort / Cancel
- [x] مدیریت نام فایل تکراری
- [x] Checksum SHA-256
- [x] محدودیت‌های امنیتی پایه (حجم، extension، session per IP)
- [x] Pause / Resume / سرعت (UI دمو)
- [x] Serilog + `/health` + `/api/metrics`

---

## 🎯 تصمیمات معماری

| موضوع | تصمیم |
|-------|--------|
| نقش در سیستم | **آداپتر آپلود** (نه مونولیت) |
| پروتکل | HTTP/2 + Chunked REST |
| gRPC | ❌ برای آپلود مرورگر مناسب نیست |
| Object Storage خارجی | ❌ فعلاً؛ پشت `IFileStorage` قابل افزودن |
| Storage پیش‌فرض | FileSystem |
| Auth | خارج از محدوده (Gateway / Identity) |
| وضعیت فایل | Pending → Completed |
| پاکسازی orphan | BackgroundJob + TTL |
| Checksum | SHA-256 اختیاری |

---

## 🚀 Backlog (فقط موارد مرتبط با آداپتر)

### اولویت‌های ۱–۳ — ✅ انجام‌شده

### 🟢 اولویت ۴ — بهبود آداپتر (اختیاری)

- [ ] سخت‌تر کردن مرز پورت‌ها (مثلاً جدا کردن UI دمو از WebApi اگر لازم شد)
- [ ] آداپتر storage جایگزین (مثلاً S3-compatible) پشت همان `IFileStorage` — فقط در صورت نیاز
- [ ] رویداد بعد از complete (مثلاً publish به bus برای virus-scan / indexing) بدون مالکیت آن منطق
- [ ] GPU-accelerated hashing (آینده، فقط اگر bottleneck واقعی شد)
- [ ] Brotli / Deflate per-chunk compression (آینده، فقط اگر bandwidth bottleneck شد)
- [ ] OpenTelemetry exporter سبک (اگر پلتفرم observability مشترک دارید)

### ⛔ عمداً انجام نمی‌شود در این ریپو

- JWT / Login / User management
- Rate limiting سطح پلتفرم
- Virus engine داخل همین process
- سرویس دانلود / ACL فایل

---

## 📌 Observability (سبک، برای خود آداپتر)

| Endpoint | توضیح |
|----------|--------|
| `GET /health` | process + DB + storage |
| `GET /api/metrics` | شمارنده‌های in-process |

---

*آخرین به‌روزرسانی: هم‌راستاسازی با معماری هگزاگونال — فقط وظیفه آپلود.*
