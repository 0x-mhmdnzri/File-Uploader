using Microsoft.EntityFrameworkCore;
using WebApi.Domain;

namespace WebApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<UploadSession> UploadSessions => Set<UploadSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<UploadSession>();

        entity.HasKey(x => x.Id);

        entity.Property(x => x.FileName)
            .IsRequired()
            .HasMaxLength(512);

        entity.Property(x => x.FinalFileName)
            .HasMaxLength(512);

        entity.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired();

        entity.Property(x => x.Version)
            .IsConcurrencyToken()
            .IsRequired()
            .HasDefaultValue(0);

        entity.Property(x => x.ReceivedChunksCsv)
            .HasMaxLength(8000);

        entity.Property(x => x.Checksum)
            .HasMaxLength(128);

        entity.Property(x => x.ContentFingerprint)
            .HasMaxLength(128);

        entity.Property(x => x.ContentType)
            .HasMaxLength(256);

        entity.Property(x => x.ClientIp)
            .HasMaxLength(64);

        // Hot filters
        entity.HasIndex(x => x.Status);
        entity.HasIndex(x => x.ExpiresAt);
        entity.HasIndex(x => new { x.Status, x.ExpiresAt });
        entity.HasIndex(x => new { x.ClientIp, x.Status });
        // Content-addressed dedupe lookups
        entity.HasIndex(x => new { x.Checksum, x.TotalSize, x.Status });
        entity.HasIndex(x => new { x.ContentFingerprint, x.TotalSize, x.Status });
        // CAS complete path: WHERE Id = @id AND Status = Pending|Completing (perf 4.3)
        // PK covers Id; composite with Status helps the filtered ExecuteUpdate plans on Postgres.
        entity.HasIndex(x => new { x.Id, x.Status });
        entity.HasIndex(x => x.FinalFileName);
    }
}
