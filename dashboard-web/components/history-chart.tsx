import type { FleetGpu } from "@/lib/types";

export function HistoryChart({ history }: { history: FleetGpu[] }) {
  const points = history.length > 1 ? history : [];
  if (!points.length) return <div className="chart-empty">History appears after the first heartbeats arrive.</div>;
  const width = 920;
  const height = 220;
  const path = points.map((item, index) => {
    const x = (index / Math.max(1, points.length - 1)) * width;
    const y = height - (Math.min(100, item.loadPct) / 100) * (height - 24) - 12;
    return `${index ? "L" : "M"}${x.toFixed(1)},${y.toFixed(1)}`;
  }).join(" ");
  const temps = points.filter((item) => item.temperatureC !== null);
  const tempPath = temps.map((item, index) => {
    const x = (index / Math.max(1, temps.length - 1)) * width;
    const y = height - (Math.min(100, item.temperatureC ?? 0) / 100) * (height - 24) - 12;
    return `${index ? "L" : "M"}${x.toFixed(1)},${y.toFixed(1)}`;
  }).join(" ");
  return (
    <div className="history-chart">
      <div className="chart-legend"><span><i className="legend-load" />GPU load</span><span><i className="legend-temp" />Temperature °C</span></div>
      <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label="GPU load and temperature history">
        {[25, 50, 75].map((line) => <line key={line} x1="0" x2={width} y1={height - line / 100 * (height - 24) - 12} y2={height - line / 100 * (height - 24) - 12} className="grid-line" />)}
        <path d={`${path} L${width},${height} L0,${height} Z`} className="load-area" />
        <path d={path} className="load-line" />
        {tempPath ? <path d={tempPath} className="temp-line" /> : null}
      </svg>
    </div>
  );
}
