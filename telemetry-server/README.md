# GPU Monitor Telemetry Server

Standard-library Python telemetry receiver backed by SQLite. It stores anonymous device health samples and generates early-warning alerts for sustained GPU temperature, VRAM pressure, system RAM pressure, fan anomalies, and GPU throttling.

Raw IP addresses and hardware serials are never stored. The server stores an HMAC of the remote IP; the widget sends a locally hashed hardware identifier.

## Run locally

```powershell
python server.py
```

The default endpoint is `http://127.0.0.1:8765/api/v1/heartbeat` and the database is created at `data/gpu_monitor.db`.

Production configuration:

```text
GPU_MONITOR_BIND=0.0.0.0
GPU_MONITOR_PORT=8765
GPU_MONITOR_DB=/var/lib/gpu-monitor/gpu_monitor.db
GPU_MONITOR_HASH_SECRET=<long-random-secret>
```

Place the service behind HTTPS and authentication before exposing it outside a trusted network.

## Read APIs

- `GET /health`
- `GET /api/v1/devices`
- `GET /api/v1/alerts?status=open`
- `POST /api/v1/heartbeat`

SQLite is suitable for a single internal server. Move to PostgreSQL before running multiple API instances or retaining high-frequency samples from thousands of devices.
