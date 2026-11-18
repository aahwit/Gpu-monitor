export function Gauge({ value, label, suffix = "%", tone = "cyan" }: { value: number; label: string; suffix?: string; tone?: string }) {
  const clamped = Math.min(100, Math.max(0, value));
  return (
    <div className={`gauge tone-${tone}`} style={{ "--value": `${clamped * 3.6}deg` } as React.CSSProperties}>
      <div className="gauge-inner"><strong>{value.toFixed(0)}{suffix}</strong><span>{label}</span></div>
    </div>
  );
}
