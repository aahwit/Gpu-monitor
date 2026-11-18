import Link from "next/link";
import { notFound } from "next/navigation";
import { Activity, ArrowLeft, Box, Cpu, Fan, Gauge as GaugeIcon, MemoryStick, Radio, Zap } from "lucide-react";
import { AutoRefresh } from "@/components/auto-refresh";
import { Gauge } from "@/components/gauge";
import { HeatBadge } from "@/components/heat-badge";
import { HistoryChart } from "@/components/history-chart";
import { getDevice, getHistory } from "@/lib/storage";
import { bytesToGiB, heatLevel, isOnline, memoryPercent } from "@/lib/types";

export const dynamic = "force-dynamic";

export default async function DevicePage({ params }: { params: Promise<{ deviceId: string }> }) {
  const { deviceId } = await params;
  const id = decodeURIComponent(deviceId);
  const [device, history] = await Promise.all([getDevice(id), getHistory(id)]);
  if (!device) notFound();
  const online = isOnline(device);
  const vram = memoryPercent(device.vramUsedBytes, device.vramTotalBytes);
  const ram = memoryPercent(device.ramUsedBytes, device.ramTotalBytes);
  const heat = heatLevel(device.temperatureC);

  return (
    <main className="dashboard-page detail-page">
      <header className="topbar"><div className="brand"><span className="brand-mark"><Activity size={19} /></span><div><strong>GPU Fleet Control</strong><small>Device telemetry</small></div></div><div className="top-actions"><span className={`live-pill ${online ? "" : "offline-pill"}`}><span className="status-dot" />{online ? "Online" : "Offline"}</span><AutoRefresh /></div></header>
      <div className="page-wrap">
        <Link href="/" className="back-link"><ArrowLeft size={16} />Back to fleet</Link>
        <section className={`device-hero heat-surface-${heat}`}>
          <div className="device-title"><p className="eyebrow">GPU {device.gpuIndex} · {device.id.slice(-12)}</p><h1>{device.name}</h1><div className="hero-meta"><HeatBadge device={device} /><span>Driver {device.driver}</span><span>App v{device.appVersion}</span></div></div>
          <div className="hero-gauges"><Gauge value={device.loadPct} label="GPU workload" tone={heat} /><Gauge value={vram} label="GPU memory" tone={vram >= 90 ? "danger" : "cyan"} /></div>
        </section>

        <section className="detail-metrics">
          <article><span><GaugeIcon size={17} />Temperature</span><strong>{device.temperatureC === null ? "N/A" : `${device.temperatureC.toFixed(0)}°C`}</strong><small>{device.temperatureC === null ? "Sensor unavailable" : heat.toUpperCase()}</small></article>
          <article><span><MemoryStick size={17} />GPU memory</span><strong>{bytesToGiB(device.vramUsedBytes).toFixed(1)} GB</strong><small>of {bytesToGiB(device.vramTotalBytes).toFixed(1)} GB · {vram.toFixed(0)}%</small></article>
          <article><span><Zap size={17} />Board power</span><strong>{device.powerW === null ? "N/A" : `${device.powerW.toFixed(0)} W`}</strong><small>{device.powerLimitW === null ? "Limit unavailable" : `of ${device.powerLimitW.toFixed(0)} W limit`}</small></article>
          <article><span><Fan size={17} />Fan speed</span><strong>{device.fanPct === null ? "N/A" : `${device.fanPct.toFixed(0)}%`}</strong><small>{device.throttle.length ? `Throttle: ${device.throttle.join(", ")}` : "No throttling"}</small></article>
          <article><span><Cpu size={17} />System CPU</span><strong>{device.cpuLoadPct.toFixed(0)}%</strong><small>Current host load</small></article>
          <article><span><Box size={17} />System RAM</span><strong>{ram.toFixed(0)}%</strong><small>{bytesToGiB(device.ramUsedBytes).toFixed(1)} / {bytesToGiB(device.ramTotalBytes).toFixed(0)} GB</small></article>
        </section>

        <section className="detail-grid">
          <div className="panel history-panel"><div className="panel-heading"><div><p className="eyebrow">RECENT SIGNAL</p><h2>Workload & thermal history</h2></div><Radio size={20} /></div><HistoryChart history={history} /></div>
          <aside className="panel device-info"><div className="panel-heading"><div><p className="eyebrow">DEVICE</p><h2>Host details</h2></div></div><dl><div><dt>Operating system</dt><dd>{device.osVersion}</dd></div><div><dt>Processes using GPU</dt><dd>{device.processCount}</dd></div><div><dt>Last heartbeat</dt><dd>{new Date(device.receivedAt).toLocaleString("en-GB")}</dd></div><div><dt>Hardware identity</dt><dd>{device.hardwareIdHash.slice(0, 16)}…</dd></div><div><dt>Network identity</dt><dd>{device.ipHash ? `${device.ipHash.slice(0, 16)}…` : "Not recorded"}</dd></div></dl></aside>
        </section>
      </div>
    </main>
  );
}
