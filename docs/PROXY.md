# HTTP/2 and reverse proxy

Parallel chunk PUTs benefit from **one multiplexed HTTP/2 connection**. Configure the edge and Kestrel accordingly.

## Kestrel

`appsettings.json` already suggests:

```json
"Kestrel": {
  "EndpointDefaults": {
    "Protocols": "Http1AndHttp2"
  }
}
```

For cleartext h2c behind a local proxy only; browsers need TLS for HTTP/2.

## nginx (TLS termination + HTTP/2 to client)

```nginx
server {
  listen 443 ssl http2;
  server_name upload.example.com;

  ssl_certificate     /etc/ssl/certs/fullchain.pem;
  ssl_certificate_key /etc/ssl/private/privkey.pem;

  client_max_body_size 100m;  # per-chunk ceiling

  location / {
    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header Connection "";
    proxy_request_buffering off;   # stream large PUTs
    proxy_pass http://127.0.0.1:5000;
  }
}
```

## Caddy

```caddy
upload.example.com {
  reverse_proxy localhost:5000 {
    transport http {
      versions 1.1 2
    }
  }
}
```

## Notes

- Keep per-chunk size ≤ proxy body limit (`client_max_body_size` / equivalent).
- Disable request buffering for upload paths when possible so the app streams to disk/S3.
- Health checks should hit `/health` without API key when auth is enabled.
