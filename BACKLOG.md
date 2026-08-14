# BACKLOG — File Uploader (personal file service)

Last updated: 2026-08-14 (`dev`) — P2 product/ops closed in code.

---

## Done

### P0 / P1 (prior)
- [x] Parallel upload stack, merge modes, StorageBench, CRC/SHA chunk integrity
- [x] Streaming client SHA-256, mid-flight adaptive workers

### P2 — product / ops (2026-08-14)
- [x] R8 — API key auth (`Auth:Enabled`, `X-Api-Key`, fixed-time compare; health/swagger anonymous)
- [x] R9 — Structured audit logger (`IAuditLogger` / Serilog `EventType=Upload*` + `logs/audit-*.log`)
- [x] R10 — Storage quotas: `MaxTotalStoredBytes`, `MaxStoredBytesPerIp` (Completed + active Pending reserved)
- [x] R11 — Bench docs/templates; host must paste measured numbers into `docs/BENCH.md` (no fabricated timings)

---

## Remaining

### P3 — deferred

| ID | Task |
|----|------|
| R12 | S3 / Azure / MinIO |
| R13 | GPU hasher |
| R14 | Rabbit/Kafka handler |
| R15 | HTTP/2 / proxy guide |

---

## Enable auth

```json
"Auth": {
  "Enabled": true,
  "ApiKey": "your-long-random-secret",
  "HeaderName": "X-Api-Key"
}
```

WebApp:

```json
"ApiKey": "your-long-random-secret"
```

Prefer env: `Auth__ApiKey`, `ApiKey`.

## Quotas

```json
"StorageOptions": {
  "MaxTotalStoredBytes": 214748364800,
  "MaxStoredBytesPerIp": 53687091200
}
```

Set to `0` to disable a limit.
