using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WebApi.Data;

/// <summary>
/// Used by <c>dotnet ef migrations</c>. Defaults to Sqlite for local tooling.
/// Runtime provider still comes from configuration (Sqlite | Postgres).
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=uploads.db")
            .Options;

        return new AppDbContext(options);
    }
}
