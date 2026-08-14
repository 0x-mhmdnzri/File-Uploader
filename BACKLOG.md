# 📋 BACKLOG — File Uploader (Custom Storage Service)

> این فایل بر اساس تحلیل معماری و وضعیت فعلی کدبیس نوشته شده.
> اولویت‌ها از بالا به پایین مرتب شده‌اند.

---

## ✅ وضعیت فعلی (Current State)

- [x] Chunked Upload با سایز قابل تنظیم (پیش‌فرض کلاینت: ۱۶ مگابایت)
- [x] آپلود موازی (Parallel Workers) در کلاینت
- [x] Resume نسبی از طریق endpoint وضعیت
- [x] ذخیره‌سازی چانک‌ها روی FileSystem
- [x] Merge نهایی چانک‌ها (sequential + stream)
- [x] معماری تمیز (Controller / Service / Storage / Repository)
- [x] Persistent Storage با EF Core + SQLite
- [x] وضعیت دو مرحله‌ای (Pending → Completed / Expired / Aborted / Failed)
- [x] Orphan Cleanup BackgroundService
- [x] Abort / Cancel endpoint
- [x] مدیریت نام فایل تکراری
- [x] Checksum (SHA-256) verification
- [x] **محدودیت‌های امنیتی پایه** (حجم، extension، session per IP)

### مشکلات و محدودیت‌های باقی‌مانده

| مشکل | توضیح |
|------|--------|
| **عدم وجود Auth** | هر کسی می‌تواند آپلود کند |
| **Resume کامل در کلاینت** | localStorage اضافه شده ولی UI کامل نیست |

---

## 🎯 تصمیمات معماری قطعی

| موضوع | تصمیم نهایی |
|-------|-------------|
| پروتکل | HTTP/2 + Chunked Upload (REST) |
| gRPC | ❌ استفاده نشود |
| Object Storage خارجی | ❌ استفاده نشود |
| ذخیره‌سازی | FileSystem + `IFileStorage` |
| وضعیت فایل | Pending → Completed |
| پاکسازی | Background Job + TTL |
| Repository | EF Core + SQLite |
| Checksum | SHA-256 (اختیاری) |

---

## 🚀 Backlog

### 🔴 اولویت ۱ — Critical — ✅ انجام شد

- [x] وضعیت دو مرحله‌ای
- [x] Cleanup Job
- [x] Persistent Storage
- [x] اصلاح Merge

### 🟠 اولویت ۲ — High

- [x] Checksum (SHA-256)
- [x] Abort / Cancel
- [x] مدیریت نام فایل تکراری
- [x] محدودیت‌های امنیتی پایه
  - [x] MaxFileSizeBytes (پیش‌فرض ۲۰GB)
  - [x] MaxChunkSizeBytes (پیش‌فرض ۳۲MB)
  - [x] BlockedExtensions / AllowedExtensions
  - [x] MaxPendingSessionsPerIp (پیش‌فرض ۵)

### 🟡 اولویت ۳ — Medium

#### 3.1 — بهبود Resume
- [x] ذخیره uploadId در localStorage
- [ ] UI کامل برای Resume بعد از رفرش صفحه

#### 3.2 — Progress و Observability
- [ ] لاگ ساختاریافته (Serilog)
- [ ] متریک‌های پایه
- [ ] Health Check endpoint

#### 3.3 — بهبود کلاینت
- [ ] نمایش سرعت آپلود (MB/s)
- [ ] دکمه Pause / Resume / Cancel در UI
- [ ] مدیریت بهتر خطاها

#### 3.4 — جداسازی Staging و Final
- [x] temp/ برای pending و uploads/ برای final

### 🟢 اولویت ۴ — Low / Future

- [ ] Authentication / Authorization (JWT یا API Key)
- [ ] Rate Limiting پیشرفته
- [ ] پشتیبانی از HTTP/3
- [ ] Distributed Upload (چند نود)
- [ ] Virus Scanning بعد از complete
- [ ] **GPU-accelerated hashing**
- [ ] **Brotli / Deflate per-chunk compression**

---

## 📌 تنظیمات امنیتی (appsettings.json)

```json
"StorageOptions": {
  "MaxFileSizeBytes": 21474836480,
  "MaxChunkSizeBytes": 33554432,
  "MaxPendingSessionsPerIp": 5,
  "AllowedExtensions": [],
  "BlockedExtensions": [ "exe", "bat", "cmd", "com", "msi", "scr", "ps1", "vbs", "js", "jar", "dll", "sh" ]
}
```

- `AllowedExtensions` خالی = همه extensionها مجاز (به‌جز Blocked)
- اگر `AllowedExtensions` پر باشد، فقط همان‌ها پذیرفته می‌شوند

---

*آخرین به‌روزرسانی: پیاده‌سازی محدودیت‌های امنیتی پایه (2.4).*
