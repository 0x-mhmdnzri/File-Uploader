# 📋 BACKLOG — File Uploader (Custom Storage Service)

> اولویت‌ها از بالا به پایین مرتب شده‌اند.

---

## ✅ وضعیت فعلی

- [x] Chunked Upload (پیش‌فرض کلاینت: ۱۶ مگابایت)
- [x] آپلود موازی (Parallel Workers)
- [x] Resume از طریق status + localStorage + UI
- [x] FileSystem storage + sequential merge
- [x] معماری تمیز (Controller / Service / Storage / Repository)
- [x] EF Core + SQLite
- [x] وضعیت دو مرحله‌ای (Pending → Completed / Expired / Aborted / Failed)
- [x] Orphan Cleanup BackgroundService
- [x] Abort / Cancel endpoint + دکمه Cancel در UI
- [x] مدیریت نام فایل تکراری
- [x] Checksum SHA-256 (کلاینت + سرور)
- [x] محدودیت‌های امنیتی (حجم، extension، session per IP)
- [x] **Pause / Resume در UI**
- [x] **نمایش سرعت آپلود (MB/s)**
- [x] **بنر Resume بعد از رفرش صفحه**

### باقی‌مانده

| موضوع | توضیح |
|------|--------|
| Auth | هنوز احراز هویت نداریم |
| Observability | Serilog / Health / Metrics |

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

---

## 🚀 Backlog

### 🔴 اولویت ۱ — Critical — ✅

- [x] وضعیت دو مرحله‌ای
- [x] Cleanup Job
- [x] Persistent Storage
- [x] اصلاح Merge

### 🟠 اولویت ۲ — High — ✅

- [x] Checksum (SHA-256)
- [x] Abort / Cancel
- [x] مدیریت نام فایل تکراری
- [x] محدودیت‌های امنیتی پایه

### 🟡 اولویت ۳ — Medium

#### 3.1 — بهبود Resume
- [x] ذخیره session در localStorage
- [x] UI Resume بعد از رفرش (بنر + دکمه Resume/Discard)

#### 3.2 — Progress و Observability
- [ ] لاگ ساختاریافته (Serilog)
- [ ] متریک‌های پایه
- [ ] Health Check endpoint

#### 3.3 — بهبود کلاینت
- [x] نمایش سرعت آپلود (MB/s)
- [x] دکمه Pause / Resume / Cancel در UI
- [x] مدیریت بهتر خطاها (پیام روی صفحه)

#### 3.4 — Staging / Final
- [x] `temp/` و `uploads/`

### 🟢 اولویت ۴ — Low / Future

- [ ] Authentication / Authorization (JWT یا API Key)
- [ ] Rate Limiting پیشرفته
- [ ] پشتیبانی از HTTP/3
- [ ] Distributed Upload (چند نود)
- [ ] Virus Scanning بعد از complete
- [ ] **GPU-accelerated hashing**
- [ ] **Brotli / Deflate per-chunk compression**

---

## 📌 تنظیمات امنیتی

```json
"StorageOptions": {
  "MaxFileSizeBytes": 21474836480,
  "MaxChunkSizeBytes": 33554432,
  "MaxPendingSessionsPerIp": 5,
  "AllowedExtensions": [],
  "BlockedExtensions": [ "exe", "bat", "cmd", "com", "msi", "scr", "ps1", "vbs", "js", "jar", "dll", "sh" ]
}
```

---

*آخرین به‌روزرسانی: Client UX (Pause/Resume/Cancel، سرعت، Resume UI) — 3.1 و 3.3 انجام شد.*
