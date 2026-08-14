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
- [x] Merge نهایی چانک‌ها
- [x] معماری نسبتاً تمیز (Controller / Service / Storage / Repository)

### مشکلات و محدودیت‌های فعلی

| مشکل | توضیح |
|------|--------|
| **In-Memory Repository** | با ریستارت سرور تمام sessionها از بین می‌روند |
| **عدم مدیریت Orphan** | فایل‌های موقت (pending) پاک نمی‌شوند و فضا اشغال می‌کنند |
| **عدم وجود Status دو مرحله‌ای** | مفهوم `pending` / `confirmed` وجود ندارد |
| **Merge با Lock** | در `FileSystemStorage.MergeAsync` از `lock` استفاده شده که scalability را کاهش می‌دهد |
| **عدم وجود Checksum** | صحت فایل نهایی تأیید نمی‌شود |
| **عدم وجود Cleanup Job** | هیچ مکانیزم پاکسازی خودکار وجود ندارد |
| **عدم وجود Auth** | هر کسی می‌تواند آپلود کند |
| **عدم پشتیبانی از Abort/Cancel** | امکان لغو آپلود وجود ندارد |
| **نام فایل تکراری** | اگر دو فایل هم‌نام آپلود شود، overwrite می‌شود |

---

## 🎯 تصمیمات معماری قطعی (Final Decisions)

این تصمیمات بر اساس بحث‌های قبلی گرفته شده و نباید تغییر کنند مگر دلیل قوی وجود داشته باشد:

| موضوع | تصمیم نهایی | دلیل |
|-------|-------------|------|
| **پروتکل** | HTTP/2 + Chunked Upload (REST) | پشتیبانی عالی مرورگر، multiplexing، سادگی |
| **gRPC** | ❌ استفاده نشود | اورهد protobuf برای باینری، پشتیبانی ضعیف مرورگر |
| **Object Storage خارجی (S3/MinIO/Garage)** | ❌ استفاده نشود | هدف پروژه ساخت Storage Service خودمان است |
| **ذخیره‌سازی** | FileSystem + طراحی قابل تعویض از طریق `IFileStorage` | سادگی + امکان مهاجرت بعدی |
| **وضعیت فایل** | دو مرحله‌ای: `Pending` → `Confirmed` | مدیریت Orphan |
| **پاکسازی** | Background Job + TTL | جلوگیری از پر شدن دیسک |
| **Repository** | از In-Memory به Persistent (SQLite یا PostgreSQL) مهاجرت کند | Production-ready |

---

## 🚀 Backlog اولویت‌بندی‌شده

### 🔴 اولویت ۱ — Critical (باید قبل از استفاده واقعی انجام شود)

#### 1.1 — اضافه کردن وضعیت دو مرحله‌ای به UploadSession
- [ ] فیلد `Status` اضافه شود: `Pending` | `Completed` | `Expired` | `Aborted`
- [ ] فیلد `ExpiresAt` اضافه شود (مثلاً ۲۴ یا ۴۸ ساعت بعد از ایجاد)
- [ ] فقط وقتی `complete` صدا زده می‌شود، وضعیت به `Completed` تغییر کند
- [ ] فایل نهایی فقط در صورت `Completed` در مسیر نهایی باقی بماند

#### 1.2 — پیاده‌سازی Cleanup Job برای فایل‌های Orphan
- [ ] یک `BackgroundService` نوشته شود که هر X ساعت اجرا شود
- [ ] sessionهای `Pending` که `ExpiresAt` گذشته‌اند را پیدا کند
- [ ] هم رکورد دیتابیس و هم پوشه temp مربوطه را حذف کند
- [ ] لاگ مناسب ثبت شود

#### 1.3 — مهاجرت از InMemory به Persistent Storage
- [ ] Entity Framework Core + SQLite (یا PostgreSQL) اضافه شود
- [ ] `UploadSession` به صورت entity مدل شود
- [ ] `InMemoryUploadRepository` با پیاده‌سازی واقعی جایگزین شود
- [ ] Migration و seed اولیه نوشته شود

#### 1.4 — اصلاح Merge برای جلوگیری از Race Condition و Lock
- [ ] Merge به صورت **sequential** و stream-based بازنویسی شود (بدون parallel write با lock)
- [ ] از `FileStream` با buffer مناسب (مثلاً ۱ مگابایت) استفاده شود
- [ ] بعد از merge موفق، پوشه temp پاک شود
- [ ] در صورت خطا در میانه merge، فایل ناقص باقی نماند

---

### 🟠 اولویت ۲ — High (کیفیت و قابلیت اطمینان)

#### 2.1 — اضافه کردن Checksum
- [ ] کلاینت بتواند hash کل فایل (مثلاً SHA-256) را بفرستد
- [ ] سرور بعد از merge، hash فایل نهایی را محاسبه و مقایسه کند
- [ ] در صورت عدم تطابق، وضعیت به `Failed` تغییر کند و فایل حذف شود

#### 2.2 — پشتیبانی از Abort / Cancel
- [ ] endpoint جدید: `DELETE /api/uploads/{id}`
- [ ] وضعیت به `Aborted` تغییر کند
- [ ] پوشه temp بلافاصله پاک شود

#### 2.3 — مدیریت نام فایل تکراری
- [ ] استراتژی مشخص شود (مثلاً اضافه کردن GUID یا timestamp به نام فایل نهایی)
- [ ] یا امکان overwrite آگاهانه با فلگ

#### 2.4 — محدودیت‌های امنیتی پایه
- [ ] محدودیت حداکثر حجم فایل (مثلاً ۲۰ گیگابایت)
- [ ] محدودیت نوع فایل (extension whitelist/blacklist)
- [ ] محدودیت تعداد session همزمان per IP یا per user (در صورت وجود auth)

---

### 🟡 اولویت ۳ — Medium (تجربه کاربری و قابلیت نگهداری)

#### 3.1 — بهبود Resume
- [ ] کلاینت بتواند با `uploadId` قبلی resume کند (حتی بعد از رفرش صفحه)
- [ ] ذخیره `uploadId` در localStorage
- [ ] endpoint status دقیق‌تر شود (لیست چانک‌های موجود + درصد)

#### 3.2 — Progress و Observability بهتر
- [ ] لاگ ساختاریافته (Serilog)
- [ ] متریک‌های پایه (تعداد آپلود فعال، حجم کل، نرخ خطا)
- [ ] Health Check endpoint

#### 3.3 — بهبود کلاینت (upload.js)
- [ ] نمایش سرعت آپلود (MB/s)
- [ ] دکمه Pause / Resume / Cancel
- [ ] مدیریت بهتر خطاها و نمایش به کاربر

#### 3.4 — جداسازی Staging و Final
- [ ] مسیر `temp/` فقط برای pending باشد
- [ ] مسیر `final/` فقط فایل‌های confirmed را نگه دارد
- [ ] امکان جابجایی فیزیکی فایل بعد از complete (یا فقط تغییر وضعیت)

---

### 🟢 اولویت ۴ — Low / Future

- [ ] پشتیبانی از Multiple Storage Backend (طراحی فعلی `IFileStorage` اجازه می‌دهد)
- [ ] Authentication / Authorization (JWT یا API Key)
- [ ] Rate Limiting
- [ ] پشتیبانی از HTTP/3 در صورت نیاز
- [ ] Distributed Upload (چند نود)
- [ ] Compression per-chunk (اختیاری)
- [ ] Virus Scanning بعد از complete

---

## 🗂 پیشنهاد ساختار مدل نهایی UploadSession

```csharp
public class UploadSession
{
    public Guid Id { get; set; }
    public string FileName { get; set; }
    public string? FinalFileName { get; set; }   // بعد از resolve کردن conflict
    public long TotalSize { get; set; }
    public int ChunkSize { get; set; }
    public int TotalChunks { get; set; }
    public UploadStatus Status { get; set; }     // Pending, Completed, Expired, Aborted, Failed
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? Checksum { get; set; }        // SHA-256
    public string? ContentType { get; set; }
    // ReceivedChunks می‌تواند در جدول جدا یا به صورت JSON ذخیره شود
}

public enum UploadStatus
{
    Pending = 0,
    Completed = 1,
    Expired = 2,
    Aborted = 3,
    Failed = 4
}
```

---

## 📌 نکات اجرایی مهم

1. **اولویت با Cleanup و Persistent Storage است.** بدون این دو مورد، سیستم در محیط واقعی غیرقابل استفاده است.
2. **Merge را sequential نگه دارید.** Parallel merge با lock معمولاً کندتر و پیچیده‌تر است.
3. **از پروتکل tus پیروی کامل نکنید** مگر اینکه بخواهید با کلاینت‌های آماده tus سازگار شوید. API فعلی ساده و کافی است.
4. **HTTP/2 را در Kestrel فعال نگه دارید** (به صورت پیش‌فرض در .NET فعال است).

---

## 📅 پیشنهاد ترتیب پیاده‌سازی

```
هفته ۱:
  ├── 1.3  Persistent Repository (EF Core + SQLite)
  ├── 1.1  Status دو مرحله‌ای
  └── 1.2  Cleanup BackgroundService

هفته ۲:
  ├── 1.4  اصلاح Merge
  ├── 2.1  Checksum
  └── 2.2  Abort/Cancel

هفته ۳:
  ├── 2.3 + 2.4  مدیریت نام فایل + محدودیت‌ها
  ├── 3.1  بهبود Resume در کلاینت
  └── 3.3  UX کلاینت
```

---

*آخرین به‌روزرسانی: بر اساس تحلیل کدبیس فعلی و تصمیمات معماری توافق‌شده.*
