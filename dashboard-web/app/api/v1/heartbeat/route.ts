import { createHash } from "node:crypto";
import { validIngestRequest } from "@/lib/auth";
import { saveHeartbeat } from "@/lib/storage";
import { heatLevel, memoryPercent, type FleetGpu } from "@/lib/types";

function numeric(value: unknown, fallback = 0) {
  const number = Number(value);
  return Number.isFinite(number) ? number : fallback;
}

function text(value: unknown, fallback = "Unknown") {
  return typeof value === "string" && value.trim() ? value.trim().slice(0, 200) : fallback;
}

export async function POST(request: Request) {
  if (!validIngestRequest(request)) return Response.json({ error: "Unauthorized" }, { status: 401 });
  try {
    const payload = await request.json();
    const gpu = payload?.gpu ?? {};
    const system = payload?.system ?? {};
    const installId = text(payload?.install_id, "");
    if (!/^[a-zA-Z0-9-]{1,64}$/.test(installId) || payload.anonymous_telemetry !== true || !gpu.name)
      return Response.json({ error: "Invalid device or missing telemetry consent" }, { status: 400 });
    const gpuIndex = Math.max(0, Math.trunc(numeric(gpu?.index)));
    const forwarded = request.headers.get("x-forwarded-for")?.split(",")[0]?.trim() ?? "";
    const ipHash = createHash("sha256").update(`${forwarded}|${process.env.INGEST_API_KEY ?? ""}`).digest("hex");
    const temperature = numeric(gpu?.temperature_c, -1);
    const device: FleetGpu = {
      id: `${installId}-${gpuIndex}`,
      installId,
      hardwareIdHash: text(payload?.hardware_id_hash, "unknown"),
      appVersion: text(payload?.app_version, "unknown"),
      osVersion: text(payload?.os_version, "Windows"),
      gpuIndex,
      name: text(gpu?.name, "Unknown GPU"),
      driver: text(gpu?.driver, "unknown"),
      vramUsedBytes: Math.max(0, numeric(gpu?.vram_used_bytes)),
      vramTotalBytes: Math.max(0, numeric(gpu?.vram_total_bytes)),
      loadPct: Math.min(100, Math.max(0, numeric(gpu?.load_pct))),
      memoryLoadPct: Math.min(100, Math.max(0, numeric(gpu?.memory_load_pct))),
      temperatureC: temperature > 0 ? temperature : null,
      powerW: numeric(gpu?.power_w) > 0 ? numeric(gpu?.power_w) : null,
      powerLimitW: numeric(gpu?.power_limit_w) > 0 ? numeric(gpu?.power_limit_w) : null,
      fanPct: numeric(gpu?.fan_pct) > 0 ? numeric(gpu?.fan_pct) : null,
      throttle: Array.isArray(gpu?.throttle) ? gpu.throttle.map((item: unknown) => text(item, "")).filter(Boolean).slice(0, 10) : [],
      processCount: Math.max(0, Math.trunc(numeric(gpu?.process_count))),
      cpuLoadPct: Math.min(100, Math.max(0, numeric(system?.cpu_load_pct))),
      ramUsedBytes: Math.max(0, numeric(system?.ram_used_bytes)),
      ramTotalBytes: Math.max(0, numeric(system?.ram_total_bytes)),
      ipHash,
      receivedAt: new Date().toISOString(),
    };
    await saveHeartbeat(device);
    const vram = memoryPercent(device.vramUsedBytes, device.vramTotalBytes);
    const ram = memoryPercent(device.ramUsedBytes, device.ramTotalBytes);
    const alerts = [
      device.temperatureC !== null && device.temperatureC >= 85 ? { alert_key: "gpu_temp_critical", severity: "critical", title: "GPU temperature critical", message: `GPU temperature is ${device.temperatureC.toFixed(0)}°C.` } : null,
      vram >= 97 ? { alert_key: "vram_critical", severity: "critical", title: "GPU memory nearly full", message: `GPU memory is ${vram.toFixed(0)}% full.` } : null,
      ram >= 97 ? { alert_key: "ram_critical", severity: "critical", title: "System memory nearly full", message: `System RAM is ${ram.toFixed(0)}% full.` } : null,
    ].filter(Boolean);
    return Response.json({ ok: true, heat: heatLevel(device.temperatureC), alerts });
  } catch (error) {
    console.error("heartbeat failed", error);
    return Response.json({ error: "Invalid heartbeat" }, { status: 400 });
  }
}
