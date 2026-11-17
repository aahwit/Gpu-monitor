PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS devices (
    install_id TEXT PRIMARY KEY,
    hardware_id_hash TEXT NOT NULL,
    ip_hash TEXT NOT NULL,
    first_seen TEXT NOT NULL,
    last_seen TEXT NOT NULL,
    app_version TEXT NOT NULL,
    os_version TEXT NOT NULL,
    gpu_name TEXT NOT NULL,
    gpu_driver TEXT NOT NULL,
    vram_total_bytes INTEGER NOT NULL,
    consent_version INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS samples (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    install_id TEXT NOT NULL REFERENCES devices(install_id) ON DELETE CASCADE,
    recorded_at TEXT NOT NULL,
    gpu_temp_c REAL,
    gpu_load_pct REAL,
    gpu_memory_load_pct REAL,
    vram_used_bytes INTEGER,
    vram_total_bytes INTEGER,
    system_ram_used_bytes INTEGER,
    system_ram_total_bytes INTEGER,
    cpu_load_pct REAL,
    power_w REAL,
    power_limit_w REAL,
    fan_pct REAL,
    throttle_json TEXT NOT NULL DEFAULT '[]',
    process_count INTEGER NOT NULL DEFAULT 0,
    app_version TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_samples_device_time ON samples(install_id, id DESC);
CREATE INDEX IF NOT EXISTS idx_samples_recorded_at ON samples(recorded_at);

CREATE TABLE IF NOT EXISTS alerts (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    install_id TEXT NOT NULL REFERENCES devices(install_id) ON DELETE CASCADE,
    alert_key TEXT NOT NULL,
    severity TEXT NOT NULL CHECK(severity IN ('info', 'warning', 'critical')),
    title TEXT NOT NULL,
    message TEXT NOT NULL,
    value REAL,
    threshold REAL,
    first_seen TEXT NOT NULL,
    last_seen TEXT NOT NULL,
    occurrences INTEGER NOT NULL DEFAULT 1,
    status TEXT NOT NULL DEFAULT 'open' CHECK(status IN ('open', 'resolved'))
);

CREATE INDEX IF NOT EXISTS idx_alerts_status_time ON alerts(status, last_seen DESC);
CREATE UNIQUE INDEX IF NOT EXISTS idx_alerts_one_open ON alerts(install_id, alert_key) WHERE status = 'open';

CREATE TABLE IF NOT EXISTS consent_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    install_id TEXT NOT NULL,
    consent_version INTEGER NOT NULL,
    auto_update INTEGER NOT NULL,
    anonymous_telemetry INTEGER NOT NULL,
    recorded_at TEXT NOT NULL
);
