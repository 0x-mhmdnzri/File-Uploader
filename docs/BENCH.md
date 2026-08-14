# Measure and harden

## StorageBench

```bash
dotnet run -c Release --project tools/StorageBench -- --size-mb 256 --chunk-mb 16 --parallelism 4 --rounds 3
dotnet run -c Release --project tools/StorageBench -- --size-mb 1024 --chunk-mb 16 --parallelism 8 --rounds 3
```

Exit code `0` = both merge strategies produced the same SHA-256.

| Winner | Set `SinglePassMergeAndHash` |
|--------|------------------------------|
| parallel+hash | `false` |
| single-pass | `true` |

## Measured results

| Host / disk | size MB | parallel avg ms | single avg ms | winner | date |
|-------------|---------|-----------------|---------------|--------|------|
| MacBook Pro 12-logical, macOS 15.7.2 (temp volume) | 1024 | 3517 | 3299 | single-pass | 2026-08-14 |

Integrity: PASS (identical SHA-256). Recommended: `SinglePassMergeAndHash: true` on this host.

## API complete timing (optional)

| Host | file size | complete ms | notes |
|------|-----------|-------------|-------|
| | | | |

See also: [BLOG-FA.md](./BLOG-FA.md) (Persian deep-dive).
