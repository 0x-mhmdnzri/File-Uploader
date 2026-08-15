# EF Core migrations

Boot now runs **`Database.MigrateAsync()`** instead of `EnsureCreated`.

## Layout

```
WebApi/Data/
  AppDbContext.cs
  AppDbContextFactory.cs          # design-time (Sqlite)
  Migrations/
    20260815120000_InitialUploadSessions.cs
    AppDbContextModelSnapshot.cs
```

## Apply

Automatic on API startup for the configured provider (`Database:Provider` = `Sqlite` | `Postgres`).

Manual:

```bash
dotnet ef database update --project WebApi
```

## Add a new migration

```bash
dotnet tool install -g dotnet-ef   # once
dotnet ef migrations add <Name> --project WebApi --output-dir Data/Migrations
```

## Upgrading from EnsureCreated lab DBs

Old Sqlite files have tables **without** `__EFMigrationsHistory`. Options:

1. **Lab wipe (simplest):** delete `uploads.db` and restart.
2. **Baseline:** create history and mark initial migration applied:

```bash
dotnet ef database update 0 --project WebApi   # if empty
# or insert into __EFMigrationsHistory after creating the table manually
```

Postgres multi-node: point both API nodes at the same connection string; only one needs to win the migrate race (EF history table handles it).

## Dual provider note

Initial migration is written with CLR types (no hardcoded `TEXT`/`INTEGER`) so Sqlite and Npgsql can both apply it. If you add provider-specific SQL, split migrations or use `migrationBuilder.ActiveProvider`.
