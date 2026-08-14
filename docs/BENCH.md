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

## Measured results (fill on your host — do not invent numbers)

| Host / disk | size MB | parallel avg ms | single avg ms | winner | date |
|-------------|---------|-----------------|---------------|--------|------|
| | 256 | | | | |
| | 1024 | | | | |

## API complete timing (optional)

Log `POST /api/uploads/{id}/complete` elapsed via Serilog request logging after a real multi-GB upload.

| Host | file size | complete ms | notes |
|------|-----------|-------------|-------|
| | | | |
