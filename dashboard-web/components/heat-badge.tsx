import { heatLevel, type FleetGpu } from "@/lib/types";

export function HeatBadge({ device }: { device: FleetGpu }) {
  const level = heatLevel(device.temperatureC);
  return (
    <span className={`heat-badge heat-${level}`}>
      <span className="status-dot" />
      {device.temperatureC === null ? "Sensor unavailable" : `${device.temperatureC.toFixed(0)}°C · ${level}`}
    </span>
  );
}
