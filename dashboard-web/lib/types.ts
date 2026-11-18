export type HeatLevel = "low" | "high" | "danger" | "unknown";

export type FleetGpu = {
  id: string;
  installId: string;
  hardwareIdHash: string;
  appVersion: string;
  osVersion: string;
  gpuIndex: number;
  name: string;
  driver: string;
  vramUsedBytes: number;
  vramTotalBytes: number;
  loadPct: number;
  memoryLoadPct: number;
  temperatureC: number | null;
  powerW: number | null;
  powerLimitW: number | null;
  fanPct: number | null;
  throttle: string[];
  processCount: number;
  cpuLoadPct: number;
  ramUsedBytes: number;
  ramTotalBytes: number;
  ipHash?: string;
  receivedAt: string;
};

export type FleetSummary = {
  devices: FleetGpu[];
  total: number;
  online: number;
  offline: number;
  heat: Record<HeatLevel, number>;
  models: Array<{ name: string; count: number; online: number }>;
};

export const ONLINE_WINDOW_MS = 150_000;

export function isOnline(device: FleetGpu, now = Date.now()) {
  return now - new Date(device.receivedAt).getTime() <= ONLINE_WINDOW_MS;
}

export function heatLevel(temperature: number | null): HeatLevel {
  if (temperature === null || temperature <= 0) return "unknown";
  if (temperature >= 85) return "danger";
  if (temperature >= 70) return "high";
  return "low";
}

export function memoryPercent(used: number, total: number) {
  return total > 0 ? Math.min(100, Math.max(0, (used / total) * 100)) : 0;
}

export function bytesToGiB(bytes: number) {
  return bytes / 1024 / 1024 / 1024;
}
