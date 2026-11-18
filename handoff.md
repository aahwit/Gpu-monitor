# GPU Monitor handoff

Since 0.7.1, an opted-in widget sends `gpu.uuid` over HTTPS when NVIDIA reports a UUID for the sampled PCI bus. The receiver keys both current data and history by a SHA-256 digest of the normalized UUID. Without a UUID the fallback remains installation and slot. Older offline records from the same installation and model are listed separately as unverified history; no historical blobs are deleted or reassigned.

Deploy `dashboard-web` as Next.js. Connect a private Vercel Blob store. Set separate random secrets: `INGEST_API_KEY`, `DASHBOARD_ACCESS_KEY` (admin login), and `DASHBOARD_SESSION_TOKEN` (server only). Redeploy after configuring environment variables. Never publish credentials in Git or installers.

On enrolled Windows machines set `GPU_MONITOR_TELEMETRY_ENDPOINT` to the full HTTPS `/api/v1/heartbeat` URL and `GPU_MONITOR_INGEST_KEY` to the ingest secret, then restart the updated widget. The old installer lacks bearer authentication. Only the selected GPU reports, identified by installation and GPU index. Process counts are sent, not names.

Acceptance: verify admin login, a consenting widget appearing online, drill-down history, and transition to offline after stopping the widget. An empty production dashboard is not evidence of working telemetry.
