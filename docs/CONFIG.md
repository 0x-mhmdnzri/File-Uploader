# Configuration reference

## StorageOptions (merge / IO / integrity / quotas)

```json
"StorageOptions": {
  "Provider": "FileSystem",
  "TempPath": "temp",
  "FinalPath": "uploads",
  "PendingTtlHours": 24,
  "CleanupIntervalMinutes": 60,
  "MaxFileSizeBytes": 21474836480,
  "MaxChunkSizeBytes": 33554432,
  "MaxPendingSessionsPerIp": 5,
  "MaxTotalStoredBytes": 214748364800,
  "MaxStoredBytesPerIp": 53687091200,
  "MaxConcurrentDiskIo": 8,
  "MergeParallelism": 4,
  "SinglePassMergeAndHash": false,
  "RequireChunkCrc32": false,
  "RequireChunkSha256": false,
  "SessionCacheTtlSeconds": 30,
  "Hasher": "Hardware",
  "AllowedExtensions": [],
  "BlockedExtensions": [ "exe", "bat", "cmd", "com", "msi", "scr", "ps1", "vbs", "js", "jar", "dll", "sh" ]
}
```

| Knob | Role |
|------|------|
| `Provider` | `FileSystem` or `S3` |
| `MaxConcurrentDiskIo` | Global gate for chunk write / merge workers. Raise until disk util is high but p99 stable. |
| `MergeParallelism` | Degree of parallel offset writes / verify. |
| `SinglePassMergeAndHash` | `true` if **complete() hash** dominates (one ordered pass). `false` (default) on **fast SSD** when assemble time dominates. Measure with `tools/StorageBench`. |
| `RequireChunkCrc32` | Require `X-Chunk-CRC32`; delete part on mismatch. |
| `RequireChunkSha256` | Require `X-Chunk-SHA256`; stronger, more CPU. |
| `SessionCacheTtlSeconds` | Hot-path session cache TTL. |
| `Hasher` | `Hardware` (OS crypto / IncrementalHash) or `Cpu` (`SHA256` stream helper). |
| `MaxTotalStoredBytes` / `MaxStoredBytesPerIp` | Quota (Completed + active Pending). `0` = unlimited. |

### Merge mode choice

- Prefer **`SinglePassMergeAndHash: true`** if complete() is hash-bound on your disk.
- Prefer **parallel merge (default `false`)** on fast SSD when assemble dominates.

```bash
dotnet run -c Release --project tools/StorageBench -- --size-mb 256 --chunk-mb 16 --parallelism 4 --rounds 3
```

## ObjectStorage (when Provider=S3)

```json
"ObjectStorage": {
  "ServiceUrl": "http://127.0.0.1:9000",
  "Region": "us-east-1",
  "Bucket": "uploads",
  "AccessKey": "minioadmin",
  "SecretKey": "minioadmin",
  "TempPrefix": "temp/",
  "FinalPrefix": "files/",
  "ForcePathStyle": true
}
```

MinIO: set `ServiceUrl` + `ForcePathStyle: true`. AWS: leave `ServiceUrl` empty.

## Auth

```json
"Auth": { "Enabled": true, "ApiKey": "<secret>", "HeaderName": "X-Api-Key" }
```

## RabbitMq

```json
"RabbitMq": { "Enabled": true, "HostName": "localhost", "Exchange": "fileuploader.events" }
```

## Kestrel HTTP/2

```json
"Kestrel": { "EndpointDefaults": { "Protocols": "Http1AndHttp2" } }
```

See `docs/PROXY.md`.
