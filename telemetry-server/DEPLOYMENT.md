# Production deployment

The local server is intentionally bound to `127.0.0.1`. For a fleet deployment:

1. Run `server.py` on one internal KENRI server with `GPU_MONITOR_BIND=127.0.0.1`.
2. Put it behind an HTTPS reverse proxy such as IIS, Caddy, or nginx.
3. Set a long random `GPU_MONITOR_HASH_SECRET` and protect the dashboard routes with company authentication.
4. Build the widget installer with the public heartbeat endpoint:

```powershell
& "$env:LOCALAPPDATA\tauri\NSIS\Bin\makensis.exe" \
  "/DTELEMETRY_ENDPOINT=https://gpu-monitor.example.com/api/v1/heartbeat" \
  installer\gpu-monitor.nsi
```

5. Back up `data/gpu_monitor.db` and `.hash-secret` together.

Do not expose the built-in HTTP server directly to the internet. The receiver intentionally rejects non-loopback plain HTTP endpoints in the widget.

SQLite is appropriate for a single internal instance and a modest fleet. Use PostgreSQL before horizontal scaling or storing high-frequency data from thousands of devices.

Health alerts identify risky operating conditions; they do not prove or predict physical hardware failure. Investigate recurring thermal, throttling, fan, and memory-pressure alerts before relying on the device for critical workloads.
