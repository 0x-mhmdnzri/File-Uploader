# 📋 BACKLOG — File Uploader (Custom Storage Service)

> این فایل بر اساس تحلیل معماری و وضعیت فعلی کدبیس نوشته شده.
> اولویت‌ها از بالا به پایین مرتب شده‌اند.

---

## ✅ وضعیت فعلی (Current State)

پروژه در حال حاضر این قابلیت‌ها را دارد:

- [x] Chunked Upload با سایز قابل تنظیم (پیش‌فرض کلاینت: ۱۶ مگابایت)
- [x] آپلود موازی (Parallel Workers) در کلاینت
- [x] Resume نسبی از طریق endpoint وضعیت
- [x] ذخیره‌سازی چانک‌ها روی FileSystem
- [x] Merge نهایی چانک‌ها (sequential + stream)
- [x] معماری تمیز (Controller / Service / Storage / Repository)
- [x] **Persistent Storage با EF Core + SQLite**
- [x] **وضعیت دو مرحله‌ای (Pending → Completed / Expired / Aborted / Failed)**
- [x] **Orphan Cleanup BackgroundService**
- [x] **Abort / Cancel endpoint**
- [x] **مدیریت نام فایل تکراری**

### مشکلات و محدودیت‌های باقی‌مانده

| مشکل | توضیح |
|------|--------|
| **عدم وجود Checksum** | صحت فایل نهایی هنوز تأیید نمی‌شود |
| **عدم وجود Auth** | هر کسی می‌تواند آپلود کند |
| **محدودیت حجم/نوع فایل** | هنوز enforce نشده |
| **Resume کامل در کلاینت** | localStorage اضافه شده ولی UI کامل نیست |

---

## 🎯 تصمیمات معماری قطعی (Final Decisions)

| موضوع | تصمیم نهایی | دلیل |
|-------|-------------|------|
| **پروتکل** | HTTP/2 + Chunked Upload (REST) | پشتیبانی عالی مرورگر، multiplexing، سادگی |
| **gRPC** | ❌ استفاده نشود | اورهد protobuf برای باینری، پشتیبانی ضعیف مرورگر |
| **Object Storage خارجی (S3/MinIO/Garage)** | ❌ استفاده نشود | هدف پروژه ساخت Storage Service خودمان است |
| **ذخیره‌سازی** | FileSystem + طراحی قابل تعویض از طریق `IFileStorage` | سادگی + امکان مهاجرت بعدی |
| **وضعیت فایل** | دو مرحله‌ای: `Pending` → `Completed` | مدیریت Orphan |
| **پاکسازی** | Background Job + TTL | جلوگیری از پر شدن دیسک |
| **Repository** | EF Core + SQLite (قابل تعویض با PostgreSQL) | Production-ready |

---

## 🚀 Backlog اولویت‌بندی‌شده

### 🔴 اولویت ۱ — Critical

#### 1.1 — اضافه کردن وضعیت دو مرحله‌ای به UploadSession
- [x] فیلد `Status` اضافه شود: `Pending` | `Completed` | `Expired` | `Aborted` | `Failed`
- [x] فیلد `ExpiresAt` اضافه شود
- [x] فقط وقتی `complete` صدا زده می‌شود، وضعیت به `Completed` تغییر کند

#### 1.2 — پیاده‌سازی Cleanup Job برای فایل‌های Orphan
- [x] `OrphanCleanupService` (BackgroundService) پیاده‌سازی شد
- [x] sessionهای `Pending` منقضی‌شده پیدا و temp folder حذف می‌شود

#### 1.3 — مهاجرت از InMemory به Persistent Storage
- [x] Entity Framework Core + SQLite
- [x] `EfUploadRepository` جایگزین InMemory شد
- [x] `EnsureCreated` در startup

#### 1.4 — اصلاح Merge
- [x] Merge به صورت **sequential** و stream-based بازنویسی شد
- [x] حذف parallel write + lock
- [x] پاک کردن temp بعد از merge موفق
- [x] مدیریت نام فایل تکراری

---

### 🟠 اولویت ۲ — High (کیفیت و قابلیت اطمینان)

#### 2.1 — اضافه کردن Checksum
- [ ] کلاینت بتواند hash کل فایل (مثلاً SHA-256) را بفرستد
- [ ] سرور بعد از merge، hash فایل نهایی را محاسبه و مقایسه کند
- [ ] در صورت عدم تطابق، وضعیت به `Failed` تغییر کند و فایل حذف شود

#### 2.2 — پشتیبانی از Abort / Cancel
- [x] endpoint: `DELETE /api/uploads/{id}`
- [x] وضعیت به `Aborted` + حذف temp

#### 2.3 — مدیریت نام فایل تکراری
- [x] در صورت وجود فایل هم‌نام، GUID به نام اضافه می‌شود

#### 2.4 — محدودیت‌های امنیتی پایه
- [ ] محدودیت حداکثر حجم فایل (مثلاً ۲۰ گیگابایت)
- [ ] محدودیت نوع فایل (extension whitelist/blacklist)
- [ ] محدودیت تعداد session همزمان per IP

---

### 🟡 اولویت ۳ — Medium

#### 3.1 — بهبود Resume
- [x] ذخیره `uploadId` در localStorage (پایه)
- [ ] UI کامل برای Resume بعد از رفرش صفحه

#### 3.2 — Progress و Observability بهتر
- [ ] لاگ ساختاریافته (Serilog)
- [ ] متریک‌های پایه
- [ ] Health Check endpoint

#### 3.3 — بهبود کلاینت (upload.js)
- [ ] نمایش سرعت آپلود (MB/s)
- [ ] دکمه Pause / Resume / Cancel در UI
- [ ] مدیریت بهتر خطاها

#### 3.4 — جداسازی Staging و Final
- [x] مسیر `temp/` برای pending و `uploads/` برای final

---

### 🟢 اولویت ۴ — Low / Future

- [ ] Authentication / Authorization (JWT یا API Key)
- [ ] Rate Limiting
- [ ] پشتیبانی از HTTP/3
- [ ] Distributed Upload (چند نود)
- [ ] Virus Scanning بعد از complete

---

## 📌 نحوه اجرا بعد از این تغییرات

```bash
cd WebApi
dotnet restore
dotnet run
```

- دیتابیس SQLite به صورت خودکار با نام `uploads.db` ساخته می‌شود.
- فایل‌های موقت در پوشه `temp/` و فایل‌های نهایی در `uploads/` ذخیره می‌شوند.
- Cleanup Job هر ۶۰ دقیقه (در Development هر ۱۰ دقیقه) اجرا می‌شود.
- TTL پیش‌فرض sessionهای Pending: ۲۴ ساعت (در Development: ۲ ساعت).

---

*آخرین به‌روزرسانی: پیاده‌سازی کامل اولویت Critical.*
