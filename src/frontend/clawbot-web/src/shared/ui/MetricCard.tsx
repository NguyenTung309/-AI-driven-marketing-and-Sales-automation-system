import { Card } from "./Card";

export type MetricTone = "success" | "warning" | "error" | "neutral";

export interface MetricCardProps {
  readonly label: string;
  readonly value: string;
  readonly delta?: string;
  readonly icon?: string; // Material Symbols name
  readonly tone?: MetricTone;
}

const DELTA_TONE: Record<MetricTone, string> = {
  success: "text-success",
  warning: "text-warning",
  error: "text-error",
  neutral: "text-on-surface-variant",
};

// Telemetry widget: large mono-ish metric value + caption + semantic delta.
export function MetricCard({ label, value, delta, icon, tone = "neutral" }: MetricCardProps) {
  return (
    <Card>
      <div className="flex items-start justify-between">
        <p className="text-label-caps uppercase text-on-surface-variant">{label}</p>
        {icon ? (
          <span aria-hidden="true" className="material-symbols-outlined text-on-surface-variant/60 text-[20px]">{icon}</span>
        ) : null}
      </div>
      <p className="text-telemetry-data mt-2">{value}</p>
      {delta ? <p className={`font-mono text-mono-status mt-1 ${DELTA_TONE[tone]}`}>{delta}</p> : null}
    </Card>
  );
}
