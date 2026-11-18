import type { FleetGpu } from "./types";

const now = Date.now();

export const demoDevices: FleetGpu[] = [
  {
    id: "demo-workstation-0", installId: "demo-workstation", hardwareIdHash: "demo-a1", appVersion: "0.7.0",
    osVersion: "Windows 11 Pro", gpuIndex: 0, name: "NVIDIA GeForce RTX 3060 12 GB", driver: "581.80",
    vramUsedBytes: 7.8 * 1024 ** 3, vramTotalBytes: 12 * 1024 ** 3, loadPct: 76, memoryLoadPct: 65,
    temperatureC: 68, powerW: 142, powerLimitW: 170, fanPct: 62, throttle: [], processCount: 7,
    cpuLoadPct: 31, ramUsedBytes: 19.4 * 1024 ** 3, ramTotalBytes: 32 * 1024 ** 3,
    receivedAt: new Date(now - 34_000).toISOString(),
  },
  {
    id: "demo-render-0", installId: "demo-render", hardwareIdHash: "demo-b2", appVersion: "0.7.0",
    osVersion: "Windows 11 Pro", gpuIndex: 0, name: "NVIDIA GeForce RTX 4090 24 GB", driver: "581.80",
    vramUsedBytes: 21.7 * 1024 ** 3, vramTotalBytes: 24 * 1024 ** 3, loadPct: 98, memoryLoadPct: 91,
    temperatureC: 88, powerW: 436, powerLimitW: 450, fanPct: 94, throttle: ["thermal"], processCount: 4,
    cpuLoadPct: 78, ramUsedBytes: 57.2 * 1024 ** 3, ramTotalBytes: 64 * 1024 ** 3,
    receivedAt: new Date(now - 18_000).toISOString(),
  },
  {
    id: "demo-office-1", installId: "demo-office", hardwareIdHash: "demo-c3", appVersion: "0.7.0",
    osVersion: "Windows 11 Pro", gpuIndex: 1, name: "Intel UHD Graphics 770", driver: "32.0.101.7088",
    vramUsedBytes: 0, vramTotalBytes: 16 * 1024 ** 3, loadPct: 4, memoryLoadPct: 1,
    temperatureC: null, powerW: null, powerLimitW: null, fanPct: null, throttle: [], processCount: 2,
    cpuLoadPct: 14, ramUsedBytes: 16.3 * 1024 ** 3, ramTotalBytes: 32 * 1024 ** 3,
    receivedAt: new Date(now - 12 * 60_000).toISOString(),
  },
  {
    id: "demo-train-0", installId: "demo-train", hardwareIdHash: "demo-d4", appVersion: "0.7.0",
    osVersion: "Windows Server 2025", gpuIndex: 0, name: "NVIDIA RTX A6000 48 GB", driver: "581.80",
    vramUsedBytes: 39.1 * 1024 ** 3, vramTotalBytes: 48 * 1024 ** 3, loadPct: 93, memoryLoadPct: 82,
    temperatureC: 77, powerW: 271, powerLimitW: 300, fanPct: 76, throttle: [], processCount: 11,
    cpuLoadPct: 61, ramUsedBytes: 88 * 1024 ** 3, ramTotalBytes: 128 * 1024 ** 3,
    receivedAt: new Date(now - 51_000).toISOString(),
  },
];
