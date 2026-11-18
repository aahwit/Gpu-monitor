import { get, list, put } from "@vercel/blob";
import { demoDevices } from "./demo";
import { heatLevel, isOnline, type FleetGpu, type FleetSummary } from "./types";

const hasBlob = () => Boolean(process.env.BLOB_READ_WRITE_TOKEN);

async function readJson<T>(pathname: string): Promise<T | null> {
  const result = await get(pathname, { access: "private", useCache: false });
  if (!result || result.statusCode !== 200) return null;
  return new Response(result.stream).json() as Promise<T>;
}

export async function saveHeartbeat(device: FleetGpu) {
  if (!hasBlob()) throw new Error("Blob storage is not configured");
  const latestPath = `latest/${device.id}.json`;
  const historyPath = `history/${device.id}/${Date.now()}.json`;
  const body = JSON.stringify(device);
  await Promise.all([
    put(latestPath, body, { access: "private", addRandomSuffix: false, allowOverwrite: true, contentType: "application/json" }),
    put(historyPath, body, { access: "private", addRandomSuffix: false, contentType: "application/json" }),
  ]);
}

export async function getDevices(): Promise<FleetGpu[]> {
  if (!hasBlob()) return process.env.NODE_ENV === "development" ? demoDevices : [];
  const devices: FleetGpu[] = [];
  let cursor: string | undefined;
  do {
    const page = await list({ prefix: "latest/", cursor, limit: 1000 });
    const rows = await Promise.all(page.blobs.map((blob) => readJson<FleetGpu>(blob.pathname)));
    devices.push(...rows.filter((row): row is FleetGpu => row !== null));
    cursor = page.hasMore ? page.cursor : undefined;
  } while (cursor);
  return devices.sort((a, b) => b.receivedAt.localeCompare(a.receivedAt));
}

export async function getDevice(id: string) {
  if (!hasBlob()) return process.env.NODE_ENV === "development" ? demoDevices.find((device) => device.id === id) ?? null : null;
  return readJson<FleetGpu>(`latest/${id}.json`);
}

export async function getHistory(id: string, limitCount = 60) {
  if (!hasBlob()) {
    if (process.env.NODE_ENV !== "development") return [];
    const device = demoDevices.find((item) => item.id === id);
    if (!device) return [];
    return Array.from({ length: 24 }, (_, index) => ({
      ...device,
      loadPct: Math.max(0, Math.min(100, device.loadPct + Math.sin(index * 0.8) * 14)),
      temperatureC: device.temperatureC === null ? null : device.temperatureC + Math.sin(index * 0.45) * 4,
      receivedAt: new Date(Date.now() - (23 - index) * 5 * 60_000).toISOString(),
    }));
  }
  const page = await list({ prefix: `history/${id}/`, limit: 1000 });
  const selected = page.blobs.toSorted((a, b) => b.uploadedAt.getTime() - a.uploadedAt.getTime()).slice(0, limitCount);
  const rows = await Promise.all(selected.map((blob) => readJson<FleetGpu>(blob.pathname)));
  return rows.filter((row): row is FleetGpu => row !== null).toSorted((a, b) => a.receivedAt.localeCompare(b.receivedAt));
}

export async function getFleetSummary(): Promise<FleetSummary> {
  const rows = await getDevices();
  const verified = rows.filter(row => row.identityKind === "uuid");
  // Keep unverified historical observations accessible without attributing them to a physical card.
  const legacyDevices = rows.filter(row => !row.identityKind && !isOnline(row) && verified.some(v => v.installId === row.installId && v.name === row.name && v.receivedAt > row.receivedAt));
  const devices = rows.filter(row => !legacyDevices.includes(row) && !(row.identityKind === "slot" && verified.some(v => v.installId === row.installId && v.gpuIndex === row.gpuIndex && v.name === row.name && v.receivedAt >= row.receivedAt)));
  const heat: FleetSummary["heat"] = { low: 0, high: 0, danger: 0, unknown: 0 };
  const models = new Map<string, { name: string; count: number; online: number }>();
  let online = 0;
  for (const device of devices) {
    const currentOnline = isOnline(device);
    if (currentOnline) online += 1;
    heat[heatLevel(device.temperatureC)] += 1;
    const model = models.get(device.name) ?? { name: device.name, count: 0, online: 0 };
    model.count += 1;
    if (currentOnline) model.online += 1;
    models.set(device.name, model);
  }
  return { devices, legacyDevices, total: devices.length, online, offline: devices.length - online, heat, models: [...models.values()].sort((a, b) => b.count - a.count) };
}
