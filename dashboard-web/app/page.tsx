import Link from "next/link";
import { Activity, AlertTriangle, Cpu, Flame, Layers3, Radio, ServerOff, ShieldCheck, ThermometerSun } from "lucide-react";
import { AutoRefresh } from "@/components/auto-refresh";
import { DeviceTable } from "@/components/device-table";
import { getFleetSummary } from "@/lib/storage";
import { heatLevel, isOnline } from "@/lib/types";

type Query = { status?: string; heat?: string; model?: string };

export const dynamic = "force-dynamic";

export default async function DashboardPage({ searchParams }: { searchParams: Promise<Query> }) {
  const [summary, query] = await Promise.all([getFleetSummary(), searchParams]);
  const filtered = summary.devices.filter((device) => {
    if (query.status && (query.status === "online") !== isOnline(device)) return false;
    if (query.heat && heatLevel(device.temperatureC) !== query.heat) return false;
    if (query.model && device.name !== query.model) return false;
    return true;
  });
  const activeFilter = query.status ?? query.heat ?? query.model;

  return (
    <main className="dashboard-page">
      <header className="topbar">
        <div className="brand"><span className="brand-mark"><Activity size={19} /></span><div><strong>GPU Fleet Control</strong><small>Health & capacity operations</small></div></div>
        <div className="top-actions"><span className="live-pill"><span className="status-dot" />Live telemetry</span><AutoRefresh /></div>
      </header>
      <div className="page-wrap">
        <section className="intro"><div><p className="eyebrow">FLEET OVERVIEW</p><h1>Every GPU. One clear signal.</h1><p>Live operating state, thermal risk, and memory pressure across the fleet.</p></div><div className="updated"><ShieldCheck size={17} />Protected operations view</div></section>

        <section className="stats-grid" aria-label="Fleet summary">
          <Link href="/" className="stat-card total"><span className="stat-icon"><Layers3 /></span><span>Total GPUs</span><strong>{summary.total}</strong><small>{summary.models.length} GPU models</small></Link>
          <Link href="/?status=online" className="stat-card online"><span className="stat-icon"><Radio /></span><span>Online</span><strong>{summary.online}</strong><small>Heartbeat within 2.5 min</small></Link>
          <Link href="/?status=offline" className="stat-card offline"><span className="stat-icon"><ServerOff /></span><span>Offline</span><strong>{summary.offline}</strong><small>Needs attention</small></Link>
          <Link href="/?heat=low" className="stat-card low"><span className="stat-icon"><ThermometerSun /></span><span>Low heat</span><strong>{summary.heat.low}</strong><small>Below 70°C</small></Link>
          <Link href="/?heat=high" className="stat-card high"><span className="stat-icon"><Flame /></span><span>High heat</span><strong>{summary.heat.high}</strong><small>70–84°C</small></Link>
          <Link href="/?heat=danger" className="stat-card danger"><span className="stat-icon"><AlertTriangle /></span><span>Danger</span><strong>{summary.heat.danger}</strong><small>85°C or higher</small></Link>
        </section>

        <section className="content-grid">
          <div className="panel fleet-panel">
            <div className="panel-heading"><div><p className="eyebrow">LIVE INVENTORY</p><h2>{activeFilter ? `Filtered GPUs · ${filtered.length}` : `GPU fleet · ${summary.total}`}</h2></div>{activeFilter ? <Link href="/" className="clear-filter">Clear filter ×</Link> : null}</div>
            <DeviceTable devices={filtered} />
          </div>
          <aside className="panel model-panel">
            <div className="panel-heading"><div><p className="eyebrow">HARDWARE MIX</p><h2>GPU types</h2></div><Cpu size={20} /></div>
            <div className="model-list">
              {summary.models.map((model) => (
                <Link href={`/?model=${encodeURIComponent(model.name)}`} key={model.name} className="model-row"><span><strong>{model.name}</strong><small>{model.online} online</small></span><b>{model.count}</b></Link>
              ))}
            </div>
            <div className="risk-note"><AlertTriangle size={17} /><span><strong>Thermal policy</strong>Danger begins at 85°C. The desktop widget alerts locally even if the network is down.</span></div>
          </aside>
        </section>
        {!!summary.legacyDevices?.length && <details className="panel"><summary>Unverified historical records ({summary.legacyDevices.length}) · excluded from fleet totals</summary><DeviceTable devices={summary.legacyDevices} /></details>}
      </div>
    </main>
  );
}
