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

        entity.Property(x => x.ReceivedChunksCsv)
            .HasMaxLength(8000);

        entity.Property(x => x.Checksum)
            .HasMaxLength(128);

        entity.Property(x => x.ContentType)
            .HasMaxLength(256);

        entity.HasIndex(x => x.Status);
        entity.HasIndex(x => x.ExpiresAt);
        entity.HasIndex(x => new { x.Status, x.ExpiresAt });
    }
}
