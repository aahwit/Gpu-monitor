import Link from "next/link";
import { ChevronRight, Cpu, MemoryStick } from "lucide-react";
import { HeatBadge } from "./heat-badge";
import { bytesToGiB, isOnline, memoryPercent, type FleetGpu } from "@/lib/types";

function ago(date: string) {
  const seconds = Math.max(0, Math.round((Date.now() - new Date(date).getTime()) / 1000));
  if (seconds < 60) return `${seconds}s ago`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m ago`;
  return `${Math.floor(seconds / 3600)}h ago`;
}

export function DeviceTable({ devices }: { devices: FleetGpu[] }) {
  if (devices.length === 0) return <div className="empty-state">No GPUs match this view.</div>;
  return (
    <div className="table-shell">
      <table>
        <thead><tr><th>GPU / device</th><th>Status</th><th>Temperature</th><th>Workload</th><th>Memory</th><th>Last seen</th><th aria-label="Open" /></tr></thead>
        <tbody>
          {devices.map((device) => {
            const online = isOnline(device);
            const vram = memoryPercent(device.vramUsedBytes, device.vramTotalBytes);
            return (
              <tr key={device.id}>
                <td><Link className="gpu-name" href={`/gpu/${encodeURIComponent(device.id)}`}><span className="gpu-icon"><Cpu size={18} /></span><span>{device.name}<small>GPU {device.gpuIndex} · {device.driver}</small></span></Link></td>
                <td><span className={`online-badge ${online ? "is-online" : "is-offline"}`}><span className="status-dot" />{online ? "Online" : "Offline"}</span></td>
                <td><HeatBadge device={device} /></td>
                <td><strong>{device.loadPct.toFixed(0)}%</strong><span className="meter"><i style={{ width: `${device.loadPct}%` }} /></span></td>
                <td><span className="memory-cell"><MemoryStick size={14} />{bytesToGiB(device.vramUsedBytes).toFixed(1)} / {bytesToGiB(device.vramTotalBytes).toFixed(0)} GB</span><span className="meter"><i style={{ width: `${vram}%` }} /></span></td>
                <td><span className="last-seen">{ago(device.receivedAt)}<small>{new Date(device.receivedAt).toLocaleString("en-GB")}</small></span></td>
                <td><Link className="row-open" href={`/gpu/${encodeURIComponent(device.id)}`} aria-label={`Open ${device.name}`}><ChevronRight size={18} /></Link></td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
