using WebApi.Domain;
using WebApi.Repositories;
using Xunit;

namespace WebApi.Tests;

/// <summary>
/// P4.5 proof tests: thin distributed coordination via CAS on the repository.
/// These run in-process against <see cref="InMemoryUploadRepository"/> and mirror
/// the Postgres ExecuteUpdate semantics used in multi-node deployments.
/// </summary>
public class CasCoordinationTests
{
    private static UploadSession NewPending(Guid? id = null, DateTime? expiresAt = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        FileName = "proof.bin",
        TotalSize = 1024,
        ChunkSize = 512,
        TotalChunks = 2,
        Status = UploadStatus.Pending,
        Version = 0,
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = expiresAt ?? DateTime.UtcNow.AddHours(2)
    };

    [Fact]
    public async Task D16_HappyPath_CompleteLease_ThenFinish()
    {
        var repo = new InMemoryUploadRepository();
        var s = NewPending();
        await repo.AddAsync(s);

        Assert.True(await repo.TryBeginCompleteAsync(s.Id));
        var mid = await repo.GetAsync(s.Id);
        Assert.NotNull(mid);
        Assert.Equal(UploadStatus.Completing, mid!.Status);

        Assert.True(await repo.TryFinishCompleteAsync(s.Id, "proof.bin", "a".PadRight(64, '0')));
        var done = await repo.GetAsync(s.Id);
        Assert.NotNull(done);
        Assert.Equal(UploadStatus.Completed, done!.Status);
        Assert.Equal("proof.bin", done.FinalFileName);
    }

    [Fact]
    public async Task D17_DoubleComplete_OnlyOneWinsBeginCas()
    {
        var repo = new InMemoryUploadRepository();
        var s = NewPending();
        await repo.AddAsync(s);

        var wins = 0;
        await Parallel.ForEachAsync(Enumerable.Range(0, 32), async (_, ct) =>
        {
            if (await repo.TryBeginCompleteAsync(s.Id, ct))
                Interlocked.Increment(ref wins);
        });

        Assert.Equal(1, wins);

        var after = await repo.GetAsync(s.Id);
        Assert.Equal(UploadStatus.Completing, after!.Status);

        // Second begin must fail; finish succeeds once.
        Assert.False(await repo.TryBeginCompleteAsync(s.Id));
        Assert.True(await repo.TryFinishCompleteAsync(s.Id, "out.bin", null));
        Assert.False(await repo.TryFinishCompleteAsync(s.Id, "out2.bin", null));
    }

    [Fact]
    public async Task D17_ParallelFinish_OnlyOneSucceeds()
    {
        var repo = new InMemoryUploadRepository();
        var s = NewPending();
        await repo.AddAsync(s);
        Assert.True(await repo.TryBeginCompleteAsync(s.Id));

        var finishes = 0;
        await Parallel.ForEachAsync(Enumerable.Range(0, 16), async (i, ct) =>
        {
            if (await repo.TryFinishCompleteAsync(s.Id, $"f{i}.bin", null, ct))
                Interlocked.Increment(ref finishes);
        });

        Assert.Equal(1, finishes);
        Assert.Equal(UploadStatus.Completed, (await repo.GetAsync(s.Id))!.Status);
    }

    [Fact]
    public async Task D18_AbortCas_OnlyOneWins()
    {
        var repo = new InMemoryUploadRepository();
        var s = NewPending();
        await repo.AddAsync(s);

        var wins = 0;
        await Parallel.ForEachAsync(Enumerable.Range(0, 24), async (_, ct) =>
        {
            if (await repo.TryAbortAsync(s.Id, ct))
                Interlocked.Increment(ref wins);
        });

        Assert.Equal(1, wins);
        Assert.Equal(UploadStatus.Aborted, (await repo.GetAsync(s.Id))!.Status);
        Assert.False(await repo.TryBeginCompleteAsync(s.Id));
    }

    [Fact]
    public async Task D18_ClaimExpired_OnlyOneNodeCleans()
    {
        var repo = new InMemoryUploadRepository();
        var s = NewPending(expiresAt: DateTime.UtcNow.AddMinutes(-5));
        await repo.AddAsync(s);

        var wins = 0;
        await Parallel.ForEachAsync(Enumerable.Range(0, 24), async (_, ct) =>
        {
            if (await repo.TryClaimExpiredAsync(s.Id, ct))
                Interlocked.Increment(ref wins);
        });

        Assert.Equal(1, wins);
        Assert.Equal(UploadStatus.Expired, (await repo.GetAsync(s.Id))!.Status);
        Assert.False(await repo.TryClaimExpiredAsync(s.Id));
    }

    [Fact]
    public async Task D18_ClaimExpired_DoesNotClaimFreshPending()
    {
        var repo = new InMemoryUploadRepository();
        var s = NewPending(expiresAt: DateTime.UtcNow.AddHours(1));
        await repo.AddAsync(s);

        Assert.False(await repo.TryClaimExpiredAsync(s.Id));
        Assert.Equal(UploadStatus.Pending, (await repo.GetAsync(s.Id))!.Status);
    }

    [Fact]
    public async Task D18_StuckCompleting_CanBeClaimedWhenExpired()
    {
        var repo = new InMemoryUploadRepository();
        var s = NewPending(expiresAt: DateTime.UtcNow.AddMinutes(-1));
        await repo.AddAsync(s);
        Assert.True(await repo.TryBeginCompleteAsync(s.Id));

        Assert.True(await repo.TryClaimExpiredAsync(s.Id));
        Assert.Equal(UploadStatus.Expired, (await repo.GetAsync(s.Id))!.Status);
    }

    [Fact]
    public async Task D17_FailComplete_FromCompleting()
    {
        var repo = new InMemoryUploadRepository();
        var s = NewPending();
        await repo.AddAsync(s);
        Assert.True(await repo.TryBeginCompleteAsync(s.Id));
        Assert.True(await repo.TryFailCompleteAsync(s.Id, null));
        Assert.Equal(UploadStatus.Failed, (await repo.GetAsync(s.Id))!.Status);
        Assert.False(await repo.TryFinishCompleteAsync(s.Id, "x", null));
    }
}
