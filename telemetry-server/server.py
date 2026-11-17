#!/usr/bin/env python3
"""GPU Monitor telemetry receiver and health-alert engine (standard library only)."""

from __future__ import annotations

import hashlib
import hmac
import json
import os
import secrets
import sqlite3
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, unquote, urlparse

ROOT = Path(__file__).resolve().parent
DB_PATH = Path(os.environ.get("GPU_MONITOR_DB", ROOT / "data" / "gpu_monitor.db"))
SCHEMA_PATH = ROOT / "schema.sql"
DASHBOARD_DIR = ROOT / "dashboard"
MAX_BODY = 64 * 1024


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def database() -> sqlite3.Connection:
    connection = sqlite3.connect(DB_PATH, timeout=10)
    connection.row_factory = sqlite3.Row
    connection.execute("PRAGMA foreign_keys = ON")
    return connection


def init_database() -> None:
    DB_PATH.parent.mkdir(parents=True, exist_ok=True)
    with database() as connection:
        connection.executescript(SCHEMA_PATH.read_text(encoding="utf-8"))


def load_hash_secret() -> bytes:
    configured = os.environ.get("GPU_MONITOR_HASH_SECRET")
    if configured:
        return configured.encode("utf-8")
    secret_path = DB_PATH.parent / ".hash-secret"
    secret_path.parent.mkdir(parents=True, exist_ok=True)
    if not secret_path.exists():
        secret_path.write_text(secrets.token_hex(32), encoding="ascii")
    return secret_path.read_text(encoding="ascii").strip().encode("ascii")


HASH_SECRET = load_hash_secret()


def hash_ip(address: str) -> str:
    return hmac.new(HASH_SECRET, address.encode("utf-8"), hashlib.sha256).hexdigest()


def number(value, default=0.0) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def integer(value, default=0) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def validate_payload(payload: dict) -> None:
    required = ("install_id", "hardware_id_hash", "app_version", "consent_version", "gpu", "system")
    missing = [name for name in required if name not in payload]
    if missing:
        raise ValueError(f"Missing fields: {', '.join(missing)}")
    if not payload.get("anonymous_telemetry"):
        raise ValueError("Anonymous telemetry consent is required")
    if len(str(payload["install_id"])) > 80 or len(str(payload["hardware_id_hash"])) > 128:
        raise ValueError("Invalid device identifier")


def upsert_device(connection: sqlite3.Connection, payload: dict, remote_ip: str, now: str) -> None:
    gpu = payload["gpu"]
    connection.execute(
        """
        INSERT INTO devices(install_id, hardware_id_hash, ip_hash, first_seen, last_seen,
                            app_version, os_version, gpu_name, gpu_driver, vram_total_bytes, consent_version)
        VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ON CONFLICT(install_id) DO UPDATE SET
            hardware_id_hash=excluded.hardware_id_hash, ip_hash=excluded.ip_hash,
            last_seen=excluded.last_seen, app_version=excluded.app_version,
            os_version=excluded.os_version, gpu_name=excluded.gpu_name,
            gpu_driver=excluded.gpu_driver, vram_total_bytes=excluded.vram_total_bytes,
            consent_version=excluded.consent_version
        """,
        (
            str(payload["install_id"]), str(payload["hardware_id_hash"]), hash_ip(remote_ip), now, now,
            str(payload["app_version"]), str(payload.get("os_version", "Windows")), str(gpu.get("name", "GPU")),
            str(gpu.get("driver", "")), integer(gpu.get("vram_total_bytes")), integer(payload["consent_version"]),
        ),
    )


def insert_sample(connection: sqlite3.Connection, payload: dict, now: str) -> None:
    gpu, system = payload["gpu"], payload["system"]
    connection.execute(
        """
        INSERT INTO samples(install_id, recorded_at, gpu_temp_c, gpu_load_pct, gpu_memory_load_pct,
            vram_used_bytes, vram_total_bytes, system_ram_used_bytes, system_ram_total_bytes,
            cpu_load_pct, power_w, power_limit_w, fan_pct, throttle_json, process_count, app_version)
        VALUES(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """,
        (
            str(payload["install_id"]), now, number(gpu.get("temperature_c")), number(gpu.get("load_pct")),
            number(gpu.get("memory_load_pct")), integer(gpu.get("vram_used_bytes")), integer(gpu.get("vram_total_bytes")),
            integer(system.get("ram_used_bytes")), integer(system.get("ram_total_bytes")), number(system.get("cpu_load_pct")),
            number(gpu.get("power_w")), number(gpu.get("power_limit_w")), number(gpu.get("fan_pct")),
            json.dumps(gpu.get("throttle", []), separators=(",", ":")), integer(gpu.get("process_count")), str(payload["app_version"]),
        ),
    )


def recent_values(connection: sqlite3.Connection, install_id: str, column: str, count: int) -> list[float]:
    allowed = {"gpu_temp_c", "gpu_load_pct", "vram_used_bytes", "vram_total_bytes", "system_ram_used_bytes", "system_ram_total_bytes", "fan_pct"}
    if column not in allowed:
        raise ValueError("Unsupported health column")
    rows = connection.execute(f"SELECT {column} FROM samples WHERE install_id=? ORDER BY id DESC LIMIT ?", (install_id, count)).fetchall()
    return [number(row[0]) for row in rows]


def set_alert(connection: sqlite3.Connection, install_id: str, key: str, active: bool, severity: str,
              title: str, message: str, value: float, threshold: float, now: str) -> None:
    open_alert = connection.execute("SELECT id FROM alerts WHERE install_id=? AND alert_key=? AND status='open'", (install_id, key)).fetchone()
    if active:
        if open_alert:
            connection.execute("UPDATE alerts SET last_seen=?, occurrences=occurrences+1, value=?, message=? WHERE id=?", (now, value, message, open_alert["id"]))
        else:
            connection.execute(
                "INSERT INTO alerts(install_id, alert_key, severity, title, message, value, threshold, first_seen, last_seen) VALUES(?,?,?,?,?,?,?,?,?)",
                (install_id, key, severity, title, message, value, threshold, now, now),
            )
    elif open_alert:
        connection.execute("UPDATE alerts SET status='resolved', last_seen=? WHERE id=?", (now, open_alert["id"]))


def all_recent_at_least(connection: sqlite3.Connection, install_id: str, column: str, threshold: float, count: int) -> bool:
    values = recent_values(connection, install_id, column, count)
    return len(values) == count and all(value >= threshold for value in values)


def ratio_percent(used, total) -> float:
    return number(used) / number(total) * 100 if number(total) > 0 else 0


def evaluate_health(connection: sqlite3.Connection, payload: dict, now: str) -> list[dict]:
    install_id = str(payload["install_id"])
    gpu, system = payload["gpu"], payload["system"]
    temp = number(gpu.get("temperature_c"))
    gpu_load = number(gpu.get("load_pct"))
    fan = number(gpu.get("fan_pct"))
    vram_pct = ratio_percent(gpu.get("vram_used_bytes"), gpu.get("vram_total_bytes"))
    ram_pct = ratio_percent(system.get("ram_used_bytes"), system.get("ram_total_bytes"))
    throttle = gpu.get("throttle") or []

    critical_temp = temp >= 87
    set_alert(connection, install_id, "gpu_temp_critical", critical_temp, "critical", "GPU temperature critical",
              f"GPU temperature is {temp:.0f}°C. Stop heavy workloads and check cooling immediately.", temp, 87, now)
    warning_temp = not critical_temp and all_recent_at_least(connection, install_id, "gpu_temp_c", 78, 3)
    set_alert(connection, install_id, "gpu_temp_warning", warning_temp, "warning", "GPU temperature high",
              f"GPU temperature remained at or above 78°C and is currently {temp:.0f}°C.", temp, 78, now)

    set_alert(connection, install_id, "vram_critical", vram_pct >= 97, "critical", "GPU memory nearly full",
              f"VRAM usage is {vram_pct:.1f}%. Applications may fail or fall back to system RAM.", vram_pct, 97, now)
    vram_warning = vram_pct < 97 and all_recent_at_least(connection, install_id, "vram_used_bytes", number(gpu.get("vram_total_bytes")) * 0.90, 3)
    set_alert(connection, install_id, "vram_warning", vram_warning, "warning", "GPU memory pressure",
              f"VRAM usage remained above 90% and is currently {vram_pct:.1f}%.", vram_pct, 90, now)

    set_alert(connection, install_id, "ram_critical", ram_pct >= 97, "critical", "System memory nearly full",
              f"System RAM usage is {ram_pct:.1f}%. Save work and close memory-heavy applications.", ram_pct, 97, now)
    ram_warning = ram_pct < 97 and all_recent_at_least(connection, install_id, "system_ram_used_bytes", number(system.get("ram_total_bytes")) * 0.90, 3)
    set_alert(connection, install_id, "ram_warning", ram_warning, "warning", "System memory pressure",
              f"System RAM usage remained above 90% and is currently {ram_pct:.1f}%.", ram_pct, 90, now)

    fan_problem = temp >= 70 and gpu_load >= 50 and fan <= 1
    set_alert(connection, install_id, "fan_not_spinning", fan_problem, "warning", "GPU fan may not be spinning",
              f"GPU load is {gpu_load:.0f}% at {temp:.0f}°C, but fan speed is {fan:.0f}%.", fan, 1, now)
    set_alert(connection, install_id, "gpu_throttling", bool(throttle), "warning", "GPU throttling detected",
              f"GPU reported throttling: {', '.join(map(str, throttle))}.", 1 if throttle else 0, 0, now)

    rows = connection.execute(
        "SELECT alert_key, severity, title, message, value, threshold, first_seen, last_seen, occurrences FROM alerts WHERE install_id=? AND status='open' ORDER BY CASE severity WHEN 'critical' THEN 0 WHEN 'warning' THEN 1 ELSE 2 END, last_seen DESC",
        (install_id,),
    ).fetchall()
    return [dict(row) for row in rows]


def receive_heartbeat(payload: dict, remote_ip: str) -> list[dict]:
    validate_payload(payload)
    now = utc_now()
    with database() as connection:
        upsert_device(connection, payload, remote_ip, now)
        previous_consent = connection.execute(
            "SELECT consent_version, auto_update, anonymous_telemetry FROM consent_events WHERE install_id=? ORDER BY id DESC LIMIT 1",
            (str(payload["install_id"]),),
        ).fetchone()
        consent_state = (integer(payload["consent_version"]), 1 if payload.get("auto_update") else 0, 1)
        if previous_consent is None or tuple(previous_consent) != consent_state:
            connection.execute(
                "INSERT INTO consent_events(install_id, consent_version, auto_update, anonymous_telemetry, recorded_at) VALUES(?,?,?,?,?)",
                (str(payload["install_id"]), *consent_state, now),
            )
        insert_sample(connection, payload, now)
        alerts = evaluate_health(connection, payload, now)
        cutoff = (datetime.now(timezone.utc) - timedelta(days=90)).isoformat(timespec="seconds")
        connection.execute("DELETE FROM samples WHERE recorded_at < ?", (cutoff,))
    return alerts


class Handler(BaseHTTPRequestHandler):
    server_version = "GpuMonitorTelemetry/1.0"

    def send_json(self, status: int, value) -> None:
        body = json.dumps(value, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def send_file(self, path: Path, content_type: str) -> None:
        if not path.is_file():
            self.send_json(404, {"error": "not_found"})
            return
        body = path.read_bytes()
        self.send_response(200)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-cache")
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self) -> None:
        if urlparse(self.path).path != "/api/v1/heartbeat":
            self.send_json(404, {"error": "not_found"})
            return
        length = integer(self.headers.get("Content-Length"))
        if length <= 0 or length > MAX_BODY:
            self.send_json(413, {"error": "invalid_body_size"})
            return
        try:
            payload = json.loads(self.rfile.read(length))
            alerts = receive_heartbeat(payload, self.client_address[0])
            self.send_json(200, {"accepted": True, "server_time": utc_now(), "alerts": alerts})
        except (ValueError, json.JSONDecodeError) as error:
            self.send_json(400, {"error": str(error)})
        except Exception:
            self.send_json(500, {"error": "internal_server_error"})

    def do_GET(self) -> None:
        parsed = urlparse(self.path)
        if parsed.path in ("/", "/index.html"):
            self.send_file(DASHBOARD_DIR / "index.html", "text/html; charset=utf-8")
            return
        if parsed.path == "/dashboard.js":
            self.send_file(DASHBOARD_DIR / "dashboard.js", "text/javascript; charset=utf-8")
            return
        if parsed.path == "/styles.css":
            self.send_file(DASHBOARD_DIR / "styles.css", "text/css; charset=utf-8")
            return
        if parsed.path == "/health":
            self.send_json(200, {"status": "ok", "time": utc_now()})
            return
        if parsed.path == "/api/v1/summary":
            with database() as connection:
                seen_rows = connection.execute("SELECT last_seen FROM devices").fetchall()
                critical = connection.execute("SELECT COUNT(*) FROM alerts WHERE status='open' AND severity='critical'").fetchone()[0]
                warning = connection.execute("SELECT COUNT(*) FROM alerts WHERE status='open' AND severity='warning'").fetchone()[0]
                average = connection.execute(
                    "SELECT AVG(gpu_temp_c) FROM samples WHERE id IN (SELECT MAX(id) FROM samples GROUP BY install_id)"
                ).fetchone()[0]
            now = datetime.now(timezone.utc)
            online = 0
            for row in seen_rows:
                try:
                    online += (now - datetime.fromisoformat(row["last_seen"])).total_seconds() <= 180
                except (TypeError, ValueError):
                    pass
            self.send_json(200, {"total": len(seen_rows), "online": online,
                                 "critical_alerts": critical, "warning_alerts": warning,
                                 "average_temperature_c": round(average or 0, 1), "server_time": utc_now()})
            return
        if parsed.path == "/api/v1/devices":
            with database() as connection:
                rows = connection.execute(
                    """SELECT d.install_id, d.hardware_id_hash, d.first_seen, d.last_seen, d.app_version,
                              d.os_version, d.gpu_name, d.gpu_driver, d.vram_total_bytes,
                              s.gpu_temp_c, s.gpu_load_pct, s.gpu_memory_load_pct, s.vram_used_bytes,
                              s.system_ram_used_bytes, s.system_ram_total_bytes, s.cpu_load_pct,
                              s.power_w, s.fan_pct,
                              (SELECT COUNT(*) FROM alerts a WHERE a.install_id=d.install_id AND a.status='open') alert_count,
                              (SELECT severity FROM alerts a WHERE a.install_id=d.install_id AND a.status='open'
                               ORDER BY CASE severity WHEN 'critical' THEN 0 WHEN 'warning' THEN 1 ELSE 2 END LIMIT 1) alert_severity
                       FROM devices d LEFT JOIN samples s ON s.id=(SELECT id FROM samples WHERE install_id=d.install_id ORDER BY id DESC LIMIT 1)
                       ORDER BY d.last_seen DESC LIMIT 500"""
                ).fetchall()
            devices = []
            now = datetime.now(timezone.utc)
            for row in rows:
                item = dict(row)
                try:
                    last_seen = datetime.fromisoformat(item["last_seen"])
                    item["online"] = (now - last_seen).total_seconds() <= 180
                except (TypeError, ValueError):
                    item["online"] = False
                item["hardware_id_short"] = item.pop("hardware_id_hash", "")[:12]
                devices.append(item)
            self.send_json(200, {"devices": devices})
            return
        if parsed.path.startswith("/api/v1/devices/") and parsed.path.endswith("/history"):
            install_id = unquote(parsed.path[len("/api/v1/devices/"):-len("/history")]).strip("/")
            hours = max(1, min(168, integer(parse_qs(parsed.query).get("hours", [24])[0], 24)))
            cutoff = (datetime.now(timezone.utc) - timedelta(hours=hours)).isoformat(timespec="seconds")
            with database() as connection:
                rows = connection.execute(
                    """SELECT recorded_at, gpu_temp_c, gpu_load_pct, gpu_memory_load_pct,
                              vram_used_bytes, vram_total_bytes, system_ram_used_bytes,
                              system_ram_total_bytes, cpu_load_pct, power_w, fan_pct
                       FROM samples WHERE install_id=? AND recorded_at>=? ORDER BY id ASC LIMIT 5000""",
                    (install_id, cutoff),
                ).fetchall()
            self.send_json(200, {"install_id": install_id, "hours": hours, "samples": [dict(row) for row in rows]})
            return
        if parsed.path == "/api/v1/alerts":
            status = parse_qs(parsed.query).get("status", ["open"])[0]
            if status not in ("open", "resolved"):
                self.send_json(400, {"error": "invalid_status"})
                return
            with database() as connection:
                rows = connection.execute("SELECT * FROM alerts WHERE status=? ORDER BY last_seen DESC LIMIT 1000", (status,)).fetchall()
            self.send_json(200, {"alerts": [dict(row) for row in rows]})
            return
        self.send_json(404, {"error": "not_found"})

    def log_message(self, format: str, *args) -> None:
        print(f"{self.address_string()} - {format % args}")


def main() -> None:
    init_database()
    host = os.environ.get("GPU_MONITOR_BIND", "127.0.0.1")
    port = integer(os.environ.get("GPU_MONITOR_PORT"), 8765)
    print(f"GPU Monitor telemetry server listening on http://{host}:{port}")
    print(f"Database: {DB_PATH}")
    ThreadingHTTPServer((host, port), Handler).serve_forever()


if __name__ == "__main__":
    main()
