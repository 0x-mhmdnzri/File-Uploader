# HTTP/2, reverse proxy & load balancer (P4.4)

Parallel chunk PUTs benefit from **one multiplexed HTTP/2 connection** to the edge.  
Multi-node correctness does **not** depend on sticky sessions — see [CLIENT-CONTRACT.md](./CLIENT-CONTRACT.md) and [MULTI-INSTANCE.md](./MULTI-INSTANCE.md).

## Health endpoints (for LB / k8s)

| Path | Purpose | Checks | Typical use |
|------|---------|--------|-------------|
| `GET /health/live` | Process up | `self` only | k8s **livenessProbe** |
| `GET /health/ready` | Accept traffic | **database** + **storage** (writable Temp/Final) | LB pool / k8s **readinessProbe** |
| `GET /health` | Aggregate | live + ready | Humans / dashboards |

- Ready returns **503** if DB or shared volume probe fails → instance is removed from the pool.
- Paths under `/health` are anonymous when API-key auth is enabled (`AnonymousPathPrefixes`).

### Example probe response

```json
{
  "status": "Healthy",
  "totalDurationMs": 12.4,
  "entries": {
    "database": { "status": "Healthy", "durationMs": 3.1 },
    "storage": {
      "status": "Healthy",
      "description": "Storage directories are writable",
      "data": { "tempPath": "/mnt/uploader/temp", "finalPath": "/mnt/uploader/uploads" }
    }
  }
}
```

## Load balancer rules (required for multi-node)

1. **Health check** = `GET /health/ready` (not only TCP open).
2. **No session affinity** required for correctness (NG2). Optional affinity is fine for cache hit rate only.
3. All upstreams share **Postgres** + the **same TempPath/FinalPath** mount.
4. Drain: stop readiness (or SIGTERM) before killing in-flight merges when possible.

### nginx upstream (no sticky)

```nginx
upstream uploader_api {
  least_conn;
  server 10.0.1.11:5000 max_fails=3 fail_timeout=10s;
  server 10.0.1.12:5000 max_fails=3 fail_timeout=10s;
}

server {
  listen 443 ssl http2;
  server_name upload.example.com;

  ssl_certificate     /etc/ssl/certs/fullchain.pem;
  ssl_certificate_key /etc/ssl/private/privkey.pem;

  client_max_body_size 100m;

  location /health/ {
    proxy_pass http://uploader_api;
    proxy_http_version 1.1;
  }

  location / {
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header Connection "";
    proxy_request_buffering off;
    proxy_pass http://uploader_api;
  }
}
```

Configure the external LB (or nginx `http_healthcheck` module / sidecar) to poll `/health/ready`.

### Kubernetes sketch

```yaml
livenessProbe:
  httpGet:
    path: /health/live
    port: 8080
  periodSeconds: 10
readinessProbe:
  httpGet:
    path: /health/ready
    port: 8080
  periodSeconds: 5
```

## Kestrel

```json
"Kestrel": {
  "EndpointDefaults": {
    "Protocols": "Http1AndHttp2"
  }
}
```

Browsers need TLS for HTTP/2; h2c only behind a local trusted proxy.

## Caddy

```caddy
upload.example.com {
  reverse_proxy 10.0.1.11:5000 10.0.1.12:5000 {
    health_uri /health/ready
    health_interval 5s
    transport http {
      versions 1.1 2
    }
  }
}
```

## Notes

- Keep per-chunk size ≤ proxy body limit (`client_max_body_size` / equivalent).
- Disable request buffering for upload paths when possible so the app streams to disk.
- See [CLIENT-CONTRACT.md](./CLIENT-CONTRACT.md) for retry and complete semantics.
