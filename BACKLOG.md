# 📋 BACKLOG — File Uploader (Custom Storage Service)

> اولویت‌ها از بالا به پایین مرتب شده‌اند.

---

## ✅ وضعیت فعلی

- [x] Chunked Upload + Parallel Workers
- [x] Resume (status + localStorage + UI)
- [x] FileSystem storage + sequential merge
- [x] معماری تمیز + EF Core / SQLite
- [x] Pending → Completed / Expired / Aborted / Failed
- [x] Orphan Cleanup BackgroundService
- [x] Abort / Cancel (API + UI)
- [x] مدیریت نام فایل تکراری
- [x] Checksum SHA-256
- [x] محدودیت‌های امنیتی پایه
- [x] Pause / Resume / سرعت / بنر Resume
- [x] **Serilog (Console + File)**
- [x] **Health Check (`/health`)**
- [x] **متریک‌های پایه (`/api/metrics`)**

### باقی‌مانده

| موضوع | توضیح |
|------|--------|
| Auth | JWT / API Key |

---

## 🎯 تصمیمات معماری

| موضوع | تصمیم |
|-------|--------|
| پروتکل | HTTP/2 + Chunked REST |
| gRPC | ❌ |
| Object Storage خارجی | ❌ |
| Storage | FileSystem + `IFileStorage` |
| وضعیت | Pending → Completed |
| پاکسازی | BackgroundJob + TTL |
| DB | EF Core + SQLite |
| Checksum | SHA-256 اختیاری |
| Logging | Serilog |

---

## 🚀 Backlog

### 🔴 اولویت ۱ — Critical — ✅
### 🟠 اولویت ۲ — High — ✅

### 🟡 اولویت ۳ — Medium — ✅

- [x] 3.1 Resume UI
- [x] 3.2 Observability (Serilog / Health / Metrics)
- [x] 3.3 Client UX
- [x] 3.4 Staging / Final

### 🟢 اولویت ۴ — Low / Future

- [ ] Authentication / Authorization (JWT یا API Key)
- [ ] Rate Limiting پیشرفته
- [ ] پشتیبانی از HTTP/3
- [ ] Distributed Upload (چند نود)
- [ ] Virus Scanning بعد از complete
- [ ] GPU-accelerated hashing
- [ ] Brotli / Deflate per-chunk compression
- [ ] Prometheus / OpenTelemetry exporter (در صورت نیاز production)

---

## 📌 Observability

| Endpoint | توضیح |
|----------|--------|
| `GET /health` | وضعیت process + DB + storage |
| `GET /api/metrics` | شمارنده‌های in-process آپلود |

لاگ‌ها:
- Console
- فایل روزانه: `logs/uploader-YYYYMMDD.log` (۱۴ روز نگه‌داری)

نمونه پاسخ metrics:

```json
{
  "initiated": 12,
  "completed": 10,
  "failed": 1,
  "aborted": 1,
  "chunksUploaded": 340,
  "bytesCompleted": 524288000,
  "since": "2026-08-14T07:00:00Z"
}
```

---

*آخرین به‌روزرسانی: Observability (3.2) — Serilog + Health + Metrics.*
