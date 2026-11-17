import { useCallback, useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import { invoke } from "@tauri-apps/api/core";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { GripHorizontal, RefreshCw, Thermometer, X, Zap } from "lucide-react";
import "./styles.css";

type GpuSnapshot = {
  gpu: string;
  vram_total: number;
  vram_used: number;
  device: { util_gpu: number; temp: number; power_w: number };
};
const percent = (used: number, total: number) => total ? Math.min(100, Math.max(0, used / total * 100)) : 0;
const gib = (bytes: number) => bytes / 1024 ** 3;

function App() {
  const [data, setData] = useState<GpuSnapshot | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const refresh = useCallback(async () => {
    try { setData(await invoke<GpuSnapshot>("get_gpu_snapshot")); setError(""); }
    catch (cause) { setError(String(cause)); }
    finally { setLoading(false); }
  }, []);
  useEffect(() => {
    void refresh();
    const timer = window.setInterval(() => void refresh(), 2000);
    return () => window.clearInterval(timer);
  }, [refresh]);
  const gpuLoad = Math.round(data?.device.util_gpu || 0);
  const vramLoad = data ? percent(data.vram_used, data.vram_total) : 0;
  return <main className="widget">
    <header data-tauri-drag-region>
      <div className="title" data-tauri-drag-region><GripHorizontal /><span data-tauri-drag-region>GPU MONITOR</span></div>
      <div className="actions">
        <button className={loading ? "spin" : ""} onClick={() => void refresh()} aria-label="Refresh"><RefreshCw /></button>
        <button onClick={() => getCurrentWindow().hide()} aria-label="Close"><X /></button>
      </div>
    </header>
    {error ? <div className="error">{error}</div> : data ? <section className="content">
      <div className="gpu-name">{data.gpu}</div>
      <div className="primary">
        <div className="gpu-icon" aria-hidden="true"><div className="fan"><i /><i /><i /></div><span>GPU</span></div>
        <div className="temperature"><Thermometer /><strong>{data.device.temp ?? "—"}°</strong><span>อุณหภูมิ</span></div>
      </div>
      <div className="load-row"><div><span>การทำงาน</span><strong>{gpuLoad}%</strong></div><div className="bar"><i style={{ width: `${gpuLoad}%` }} /></div></div>
      <div className="mini-grid">
        <div><span>VRAM</span><b>{gib(data.vram_used).toFixed(1)} / {gib(data.vram_total).toFixed(0)} GB</b><small>{vramLoad.toFixed(0)}%</small></div>
        <div><Zap /><span>พลังงาน</span><b>{(data.device.power_w || 0).toFixed(0)} W</b></div>
      </div>
      <footer>อัปเดตทุก 2 วินาที</footer>
    </section> : <div className="loading">กำลังอ่านข้อมูล GPU…</div>}
  </main>;
}
createRoot(document.getElementById("root")!).render(<App />);
