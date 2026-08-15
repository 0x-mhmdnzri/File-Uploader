using Microsoft.EntityFrameworkCore;

namespace WebApi.Data;

/// <summary>
/// Applies EF migrations when available; falls back to EnsureCreated for lab DBs
/// when no migrations are discovered or the schema was never created.
/// </summary>
public static class DatabaseBootstrap
{
    public static async Task EnsureSchemaAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();

        logger.LogInformation(
            "EF migrations: applied={AppliedCount}, pending={PendingCount} ({Pending})",
            applied.Count,
            pending.Count,
            pending.Count == 0 ? "none" : string.Join(", ", pending));

        if (pending.Count > 0 || applied.Count > 0)
        {
            await db.Database.MigrateAsync(ct);
            logger.LogInformation("Database.MigrateAsync completed");
        }
        else
        {
            // No migration metadata in this assembly/runtime — still create schema for lab.
            logger.LogWarning(
                "No EF migrations discovered; using EnsureCreated for schema (lab/dev fallback)");
            await db.Database.EnsureCreatedAsync(ct);
        }

        // Safety net: empty Sqlite file + empty history can leave zero tables.
        if (!await TableExistsAsync(db, "UploadSessions", ct))
        {
            logger.LogWarning("UploadSessions missing after migrate; EnsureCreated fallback");
            await db.Database.EnsureCreatedAsync(ct);
        }

        if (!await TableExistsAsync(db, "UploadSessions", ct))
        {
            throw new InvalidOperationException(
                "Failed to create UploadSessions table. Delete uploads.db and restart, or run: dotnet ef database update --project WebApi");
        }

        logger.LogInformation("Database schema ready (UploadSessions present)");
    }

    private static async Task<bool> TableExistsAsync(AppDbContext db, string tableName, CancellationToken ct)
    {
        try
        {
            // Works on Sqlite and Postgres: try a cheap existence probe via the model set.
            await db.UploadSessions.AsNoTracking().AnyAsync(ct);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
