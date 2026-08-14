# P0 measure and harden

## What was hardened in code

- Parallel merge validates each part length vs expected (last chunk short size).
- Parallel merge verifies final file length after workers complete.
- Single-pass merge counts written bytes vs `totalSize`.
- CRC32 mismatch deletes the bad `.part` via `DeleteChunkAsync` before 400.
- Obsolete `UploadCleanupService` removed from the tree.

## StorageBench (local)

Compares **parallel offset write + SHA** vs **single-pass merge+hash** on the machine disk.

```bash
# from repo root
dotnet run -c Release --project tools/StorageBench -- --size-mb 256 --chunk-mb 16 --parallelism 4 --rounds 3

# heavier
dotnet run -c Release --project tools/StorageBench -- --size-mb 1024 --chunk-mb 16 --parallelism 8 --rounds 3
```

Exit code `0` = integrity pass (both strategies produce the same SHA-256).

Interpret the summary line `faster on this volume`:

| Winner | Suggested `StorageOptions:SinglePassMergeAndHash` |
|--------|-----------------------------------------------------|
| parallel+hash | `false` (default) |
| single-pass | `true` |

## Record your numbers

| Host / disk | size MB | parallel avg ms | single avg ms | winner | date |
|-------------|---------|-----------------|---------------|--------|------|
| (fill) | 256 | | | | |
| (fill) | 1024 | | | | |

## API-level timing (optional)

Run WebApi, upload a large file from WebApp, and log:

1. time to last chunk PUT  
2. time inside `complete` (verify + merge + hash)  

Use Serilog request timing on `POST .../complete` as a coarse signal.
