# از آپلود کند تا ۱ گیگابایت در حدود ۳ ثانیه: داستان Concurrent در File Uploader

اگر تا حالا از خودت پرسیدی «این ConcurrentDictionary و Parallel.ForEachAsync به چه دردی می‌خورن؟» این نوشته برای توست. نه تئوری خشک، بلکه مسیری که روی همین پروژه طی شد و در آخر با یک بنچمارک واقعی روی مک‌بوک بسته شد.

---

## مشکل از کجا شروع شد؟

آپلود فایل بزرگ معمولاً این شکلیه:

1. فایل رو تکه‌تکه (chunk) می‌کنی
2. تکه‌ها رو می‌فرستی
3. سرور آخر کار همه رو به هم می‌چسبونه (merge)
4. یک SHA-256 می‌کشه که مطمئن بشی چیزی خراب نشده

نسخه ساده‌لوحانه این کار **ترتیبی** است: یک قفل بزرگ، یک حلقه `for`، یک بافر بزرگ، و امید به اینکه دیسک و CPU صبور باشن. روی فایل‌های چند صد مگابایت تا چند گیگ، bottleneck معمولاً این‌جاست:

- **نوشتن ترتیبی روی دیسک** بدون کنترل فشار
- **ردیابی chunkها با ساختار غیر thread-safe**
- **تخصیص مداوم بافر** و فشار روی GC
- **مرتب‌سازی و شمارش missing chunkها** به شکل سریال

هدف پروژه File Uploader این بود: latency را روی client و server پایین بیاوریم، اما **disk همچنان source of truth** بماند. یعنی حافظه می‌تواند cache باشد، اما حقیقت نهایی روی فایل/آبجکت است.

---

## راه‌حل در یک جمله

به‌جای یک قفل سراسری، از **primitiveهای همزمانی دات‌نت** برای سه کار استفاده کردیم:

1. ردیابی chunk بدون lock سنگین
2. کنترل فشار I/O روی دیسک
3. موازی‌سازی verify و (در صورت نیاز) نوشتن offset-based

و بعد با `StorageBench` روی ۱ گیگابایت اندازه گرفتیم که روی **همین مک** کدام استراتژی merge برنده‌تر است.

---

## هر primitive کجا و چرا؟

### ۱) `ConcurrentDictionary` — ردیابی lock-free برای chunkهای دریافتی

**کجا:** کش سراسری chunkهای دریافت‌شده (`ReceivedChunkCache`) و مسیر hot برای `MarkChunkReceived`.

**چرا:** در آپلود موازی، چند worker هم‌زمان chunk می‌فرستند. اگر با `Dictionary` معمولی + `lock` جلو بروی، هر PUT در صف قفل گیر می‌کند. `ConcurrentDictionary` برای «اضافه کردن ایندکس chunk بدون contention سنگین» طراحی شده.

**به چه درد می‌خورد؟** وقتی ده‌ها درخواست هم‌زمان دارید و فقط می‌خواهید بگویید «chunk شماره ۱۲ رسید»، این ساختار هزینه هماهنگی را پایین می‌آورد بدون اینکه هر بار DB را بزنید.

---

### ۲) `ConcurrentBag` — پیدا کردن chunkهای گم‌شده به‌صورت موازی

**کجا:** `VerifyChunksParallelAsync` قبل از merge و complete.

**چرا:** قبل از چسباندن فایل باید بدانیم کدام part روی دیسک نیست. چند thread هم‌زمان وجود فایل را چک می‌کنند و ایندکس‌های غایب را داخل `ConcurrentBag` می‌ریزند. Bag برای «فقط اضافه کن، بعداً یک‌جا بخوان» عالی است؛ ترتیب لازم نیست.

**به چه درد می‌خورد؟** روی ۶۴ chunk (۱ گیگ با chunk ۱۶ مگ) بررسی سریال یعنی ۶۴ بار stat پشت‌سرهم. موازی‌سازی این مرحله، complete را از حالت «صبر کن تا یکی‌یکی چک کنم» درمی‌آورد.

---

### ۳) `SemaphoreSlim` — ترمز دیسک (back-pressure)

**کجا:** دروازه سراسری I/O در `FileSystemStorage` (و مشابه در مسیر S3) با `MaxConcurrentDiskIo`.

**چرا:** موازی‌سازی بی‌حد روی دیسک اغلب **کندتر** می‌شود: seek زیاد، صف کنترلر پر، و latency بد. `SemaphoreSlim` می‌گوید حداکثر N عملیات دیسک هم‌زمان. عدد را از کانفیگ می‌خوانیم (مثلاً ۸).

**به چه درد می‌خورد؟** فرقش با `lock` این است که async است و thread را بی‌خودی بلوکه نمی‌کند. یعنی هم فشار را کنترل می‌کنی، هم scalability را از دست نمی‌دهی.

---

### ۴) `ArrayPool` + `Memory` / `Span` — بافر بدون زباله اضافی

**کجا:** کپی chunk، merge، و مسیر hash.

**چرا:** برای هر chunk یک `byte[1MB]` جدید بسازی، GC در آپلود بزرگ بیدار می‌شود و pause می‌دهد. `ArrayPool.Shared.Rent` بافر را قرض می‌دهد و برمی‌گرداند. `Memory<byte>` / `Span<byte>` اجازه می‌دهند روی همان بافر slice بزنی بدون کپی اضافه.

**به چه درد می‌خورد؟** «zero-allocation» مطلق نیست، ولی فشار تخصیص را از مسیر hot برمی‌دارد. در عمل یعنی throughput پایدارتر و jitter کمتر.

---

### ۵) `Interlocked` — شمارنده اتمی حجم روی دیسک

**کجا:** جمع `BytesOnDisk` هنگام verify موازی.

**چرا:** چند thread هم‌زمان طول فایل part را جمع می‌کنند. `bytes += len` معمولی race دارد. `Interlocked.Add` بدون قفل درشت، جمع را درست نگه می‌دارد.

**به چه درد می‌خورد؟** هر جایی که «فقط یک عدد را هم‌زمان به‌روز کن» داری (counter، size، flag ساده)، اول `Interlocked` را در نظر بگیر، بعد `lock`.

---

### ۶) `Parallel.ForEachAsync` — موازی‌سازی ساخت‌یافته

**کجا:** verify موازی chunkها؛ در حالت parallel merge، نوشتن هر part روی offset خودش در فایل نهایی از پیش‌allocate شده.

**چرا:** به‌جای دستی `Task.Run` و مدیریت لیست taskها، درجه موازی‌سازی (`MaxDegreeOfParallelism`) و `CancellationToken` را تمیز کنترل می‌کنی.

**به چه درد می‌خورد؟** وقتی کارها CPU/IO محدود و مستقل‌اند (مثل «chunk i را بنویس»)، این API خوانایی و ایمنی بیشتری از thread pool خام می‌دهد.

---

## دو استراتژی merge و یک انتخاب بر اساس دیسک تو

| حالت | ایده | کی بهتر است |
|------|------|-------------|
| `SinglePassMergeAndHash: false` | فایل نهایی را از قبل اندازه بزن، هر worker روی offset خودش بنویسد، بعد SHA | وقتی **سرهم‌کردن** گران است و SSD خوب seek/پویایی دارد |
| `SinglePassMergeAndHash: true` | به‌ترتیب partها را بخوان، هم‌زمان بنویس و SHA را جلو ببر | وقتی **hash + خواندن ترتیبی** روی volume تو ارزان‌تر از scramble موازی است |

هیچ‌کدام جادو نیست. **اندازه‌گیری** تصمیم را می‌گیرد.

---

## خروجی بنچمارک واقعی (۱ گیگابایت)

محیط:

- ماشین: MacBook Pro، ۱۲ منطقی، macOS (Unix 15.7.2)
- ابزار: `tools/StorageBench`
- تنظیم: `size=1024MB`، `chunk=16MB` (۶۴ تکه)، `parallelism=8`، ۳ دور

```
StorageBench — FileUploader merge stress
  size=1024MB chunk=16MB chunks=64 parallelism=8 rounds=3
  processors=12 os=Unix 15.7.2

=== Round 1/3 ===
  writing parts... 2228 ms
  parallel+hash: 3562 ms  sha=a5ca7b9fcc2b94e7...
  single-pass:   3313 ms  sha=a5ca7b9fcc2b94e7...
  integrity: OK (hashes match)

=== Round 2/3 ===
  writing parts... 2243 ms
  parallel+hash: 3506 ms  sha=a5ca7b9fcc2b94e7...
  single-pass:   3277 ms  sha=a5ca7b9fcc2b94e7...
  integrity: OK (hashes match)

=== Round 3/3 ===
  writing parts... 2220 ms
  parallel+hash: 3483 ms  sha=a5ca7b9fcc2b94e7...
  single-pass:   3307 ms  sha=a5ca7b9fcc2b94e7...
  integrity: OK (hashes match)

=== Summary ===
  parallel+hash avg: 3517 ms  min=3483 max=3562
  single-pass avg:   3299 ms  min=3277 max=3313
  faster on this volume: single-pass
  RESULT: PASS
```

### خواندن نتیجه

- **Integrity OK:** هر دو مسیر یک SHA-256 دادند؛ یعنی offset write موازی روی این volume خراب‌کاری نکرد.
- **برنده: single-pass** با حدود **۳.۳۰s** در برابر **۳.۵۲s** برای parallel+hash (میانگین).
- اختلاف حدود **۶٪** است؛ روی این دیسک، یک عبور مرتب + hash هم‌زمان کمی ارزان‌تر از «پراکنده بنویس بعد جداگانه hash کن» تمام شد.

پیشنهاد کانفیگ برای همین سخت‌افزار:

```json
"StorageOptions": {
  "MaxConcurrentDiskIo": 8,
  "MergeParallelism": 4,
  "SinglePassMergeAndHash": true,
  "RequireChunkCrc32": false,
  "SessionCacheTtlSeconds": 30,
  "MaxFileSizeBytes": 21474836480,
  "PendingTtlHours": 24
}
```

روی SSD سرور دیگری ممکن است parallel برنده شود. بنچ را آنجا تکرار کن.

---

## مسیر کلی پروژه (از مشکل تا این عدد)

1. آپلود chunk موازی روی کلاینت با worker تطبیقی
2. اعتبارسنجی session **قبل** از نوشتن روی دیسک (جلوگیری از orphan)
3. کش session و `ConcurrentDictionary` برای مسیر داغ
4. `SemaphoreSlim` + `ArrayPool` روی ذخیره و merge
5. verify با `Parallel.ForEachAsync` + `ConcurrentBag` + `Interlocked`
6. دو حالت merge و انتخاب با بنچمارک
7. لایه‌های بعدی: CRC/SHA تکه، API key، سهمیه، S3، RabbitMQ

---

## جمع‌بندی آموزشی

| ابزار | نقش در این پروژه |
|-------|------------------|
| `ConcurrentDictionary` | ثبت رسیدن chunk بدون قفل درشت |
| `ConcurrentBag` | جمع missingها هنگام verify موازی |
| `SemaphoreSlim` | سقف فشار روی دیسک/آبجکت استوریج |
| `ArrayPool` + `Memory`/`Span` | بافر قابل‌استفاده مجدد، کمتر GC |
| `Interlocked` | جمع امن حجم |
| `Parallel.ForEachAsync` | verify و نوشتن موازی کنترل‌شده |

خواندن این‌ها «برای مصاحبه» نیست. وقتی یک گیگ را در حدود سه ثانیه merge+hash می‌کنی و هنوز hash دو مسیر یکی است، یعنی primitive درست در جای درست نشسته.

اگر فقط یک چیز از این بلاگ ببری: **موازی‌سازی بدون back-pressure و بدون اندازه‌گیری، اغلب کندتر و خطرناک‌تر از سریال تمیز است.** اول correctness (hash یکسان)، بعد اندازه بگیر، بعد knob را سفت کن.

---

*بنچمارک: `dotnet run -c Release --project tools/StorageBench -- --size-mb 1024 --chunk-mb 16 --parallelism 8 --rounds 3`*
