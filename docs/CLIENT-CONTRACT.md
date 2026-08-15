# Client contract (P4.4)

This is the **supported** behavior between browser/SDK clients, the load balancer, and any API node.

## Topology assumptions

```
Client ──► LB (no session affinity) ──► API node A|B|C
                                      │
                      Postgres (sessions + CAS)
                      Shared volume (parts + finals)
```

1. **Any healthy node** may handle any request for a given `uploadId`.
2. **Sticky / affinity is not required** and must not be treated as correctness (NG2).
3. LB must only send traffic to instances that pass **`GET /health/ready`**.

## API sequence

| Step | Method | Idempotent? | Notes |
|------|--------|-------------|--------|
| 1 | `POST /api/uploads/initiate` | No (new id each time) | Returns `uploadId`, `totalChunks`, `chunkSize` |
| 2 | `PUT /api/uploads/{id}/chunk/{i}` | **Yes** | Retry on network error; `{ idempotent: true }` if part already on shared store |
| 3 | `GET /api/uploads/{id}/status` | Yes | `received[]` from **disk listing**, not a single-node memory cache |
| 4 | `POST /api/uploads/{id}/complete` | Soft | CAS: only one node merges; already `Completed` returns path; concurrent complete → retry status |
| 5 | `DELETE /api/uploads/{id}` | Soft | CAS abort; safe if already terminal |

## Retry rules (client)

| Response | Action |
|----------|--------|
| Network / timeout / 502 / 503 | Retry same request with exponential backoff + jitter (especially chunk PUT) |
| 400 on chunk (CRC/SHA mismatch) | Re-upload that chunk only |
| 400 on complete “missing chunks” | `GET status`, upload missing indexes, complete again |
| 400 “already being completed” / CAS lost | Poll `GET status` until `Completed` or `Failed` |
| 409 initiate (rate/quota) | Back off; do not spin |

Do **not** assume the same TCP connection or the same backend instance between steps.

## Progress source of truth

- Prefer `GET /status` → `received` / `receivedCount` for UI progress after reconnect.
- Local client counters are a hint only; after a crash, rebuild the missing set from status.

## Auth

When `Auth:Enabled=true`, send `X-Api-Key` (or configured header) on all API routes.  
Health endpoints under `/health` remain anonymous by default (`AnonymousPathPrefixes`).

## What clients must not rely on

- In-process events or caches on a specific node.
- Sticky cookie / IP hash for resume correctness.
- Ordering of parallel chunk completions (only the final set of parts matters).
