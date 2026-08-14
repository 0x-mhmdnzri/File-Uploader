using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;

// P0 measure & stress: parallel offset merge vs single-pass merge+hash.
// Usage:
//   dotnet run -c Release --project tools/StorageBench -- --size-mb 256 --chunk-mb 16 --parallelism 4 --rounds 3
// Exit code 0 if all integrity checks pass.

var sizeMb = 256;
var chunkMb = 16;
var parallelism = Math.Clamp(Environment.ProcessorCount, 2, 8);
var rounds = 2;
var root = Path.Combine(Path.GetTempPath(), "fileuploader-bench");

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--size-mb" when i + 1 < args.Length: sizeMb = int.Parse(args[++i]); break;
        case "--chunk-mb" when i + 1 < args.Length: chunkMb = int.Parse(args[++i]); break;
        case "--parallelism" when i + 1 < args.Length: parallelism = int.Parse(args[++i]); break;
        case "--rounds" when i + 1 < args.Length: rounds = int.Parse(args[++i]); break;
        case "--root" when i + 1 < args.Length: root = args[++i]; break;
    }
}

var totalSize = (long)sizeMb * 1024 * 1024;
var chunkSize = chunkMb * 1024 * 1024;
var totalChunks = (int)Math.Ceiling(totalSize / (double)chunkSize);

Console.WriteLine("StorageBench — FileUploader merge stress");
Console.WriteLine($"  size={sizeMb}MB chunk={chunkMb}MB chunks={totalChunks} parallelism={parallelism} rounds={rounds}");
Console.WriteLine($"  root={root}");
Console.WriteLine($"  processors={Environment.ProcessorCount} os={Environment.OSVersion}");

Directory.CreateDirectory(root);

var parallelMs = new List<double>();
var singleMs = new List<double>();
var ok = true;

for (var round = 1; round <= rounds; round++)
{
    Console.WriteLine($"\n=== Round {round}/{rounds} ===");
    var uploadId = Guid.NewGuid();
    var partDir = Path.Combine(root, uploadId.ToString());
    Directory.CreateDirectory(partDir);

    // Deterministic pseudo-random payload per chunk (fast, repeatable).
    Console.Write("  writing parts...");
    var swParts = Stopwatch.StartNew();
    await WritePartsAsync(partDir, uploadId, totalChunks, chunkSize, totalSize);
    swParts.Stop();
    Console.WriteLine($" {swParts.ElapsedMilliseconds} ms");

    // Parallel then hash
    var finalParallel = Path.Combine(root, $"parallel-{uploadId:N}.bin");
    var swP = Stopwatch.StartNew();
    await MergeParallelAsync(partDir, uploadId, finalParallel, totalChunks, totalSize, chunkSize, parallelism);
    var hashP = await HashFileAsync(finalParallel);
    swP.Stop();
    parallelMs.Add(swP.Elapsed.TotalMilliseconds);
    Console.WriteLine($"  parallel+hash: {swP.Elapsed.TotalMilliseconds:F0} ms  sha={hashP[..16]}...");

    // Single-pass (reuse same parts — re-copy parts for fair isolation)
    var uploadId2 = Guid.NewGuid();
    var partDir2 = Path.Combine(root, uploadId2.ToString());
    Directory.CreateDirectory(partDir2);
    await WritePartsAsync(partDir2, uploadId2, totalChunks, chunkSize, totalSize);

    var finalSingle = Path.Combine(root, $"single-{uploadId2:N}.bin");
    var swS = Stopwatch.StartNew();
    var hashS = await MergeSinglePassAsync(partDir2, uploadId2, finalSingle, totalChunks, totalSize);
    swS.Stop();
    singleMs.Add(swS.Elapsed.TotalMilliseconds);
    Console.WriteLine($"  single-pass:   {swS.Elapsed.TotalMilliseconds:F0} ms  sha={hashS[..16]}...");

    if (!string.Equals(hashP, hashS, StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("  FAIL: hashes differ between merge strategies");
        ok = false;
    }
    else
    {
        Console.WriteLine("  integrity: OK (hashes match)");
    }

    // Cleanup round artifacts
    TryDelete(partDir);
    TryDelete(partDir2);
    TryDeleteFile(finalParallel);
    TryDeleteFile(finalSingle);
}

Console.WriteLine("\n=== Summary ===");
Console.WriteLine($"  parallel+hash avg: {parallelMs.Average():F0} ms  min={parallelMs.Min():F0} max={parallelMs.Max():F0}");
Console.WriteLine($"  single-pass avg:   {singleMs.Average():F0} ms  min={singleMs.Min():F0} max={singleMs.Max():F0}");

var winner = parallelMs.Average() <= singleMs.Average() ? "parallel+hash" : "single-pass";
Console.WriteLine($"  faster on this volume: {winner}");
Console.WriteLine(ok ? "  RESULT: PASS" : "  RESULT: FAIL");

return ok ? 0 : 1;

// ---------- helpers ----------

static async Task WritePartsAsync(string dir, Guid id, int totalChunks, int chunkSize, long totalSize)
{
    var buffer = ArrayPool<byte>.Shared.Rent(chunkSize);
    try
    {
        for (var i = 0; i < totalChunks; i++)
        {
            var offset = (long)i * chunkSize;
            var len = (int)Math.Min(chunkSize, totalSize - offset);
            FillPattern(buffer.AsSpan(0, len), i);

            var path = Path.Combine(dir, $"{id}.part{i}");
            await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true);
            await fs.WriteAsync(buffer.AsMemory(0, len));
        }
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}

static void FillPattern(Span<byte> span, int seed)
{
    var x = (uint)(seed * 2654435761);
    for (var i = 0; i < span.Length; i++)
    {
        x ^= x << 13; x ^= x >> 17; x ^= x << 5;
        span[i] = (byte)x;
    }
}

static async Task MergeParallelAsync(
    string folder, Guid id, string finalPath, int totalChunks, long totalSize, int chunkSize, int parallelism)
{
    await using (var pre = new FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
    {
        pre.SetLength(totalSize);
        await pre.FlushAsync();
    }

    var opts = new ParallelOptions { MaxDegreeOfParallelism = parallelism };
    var bufferSize = 1 << 20;

    await Parallel.ForEachAsync(Enumerable.Range(0, totalChunks), opts, async (i, token) =>
    {
        var partPath = Path.Combine(folder, $"{id}.part{i}");
        var offset = (long)i * chunkSize;
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            await using var partFs = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
            await using var finalFs = new FileStream(finalPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, bufferSize, true);
            finalFs.Seek(offset, SeekOrigin.Begin);

            int read;
            while ((read = await partFs.ReadAsync(buffer.AsMemory(0, bufferSize), token)) > 0)
                await finalFs.WriteAsync(buffer.AsMemory(0, read), token);

            await finalFs.FlushAsync(token);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    });

    if (new FileInfo(finalPath).Length != totalSize)
        throw new InvalidOperationException("parallel merge length mismatch");
}

static async Task<string> MergeSinglePassAsync(
    string folder, Guid id, string finalPath, int totalChunks, long totalSize)
{
    var bufferSize = 1 << 20;
    var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
    try
    {
        await using var finalFs = new FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true);
        finalFs.SetLength(totalSize);
        using var sha = SHA256.Create();
        long written = 0;

        for (var i = 0; i < totalChunks; i++)
        {
            var partPath = Path.Combine(folder, $"{id}.part{i}");
            await using var partFs = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
            int read;
            while ((read = await partFs.ReadAsync(buffer.AsMemory(0, bufferSize))) > 0)
            {
                await finalFs.WriteAsync(buffer.AsMemory(0, read));
                sha.TransformBlock(buffer, 0, read, null, 0);
                written += read;
            }
        }

        await finalFs.FlushAsync();
        if (written != totalSize)
            throw new InvalidOperationException("single-pass length mismatch");

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}

static async Task<string> HashFileAsync(string path)
{
    await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, true);
    var hash = await SHA256.HashDataAsync(fs);
    return Convert.ToHexString(hash).ToLowerInvariant();
}

static void TryDelete(string dir)
{
    try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* ignore */ }
}

static void TryDeleteFile(string path)
{
    try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
}
