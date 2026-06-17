import { Card, StatusPill, type StatusTone } from "@/shared/ui";

interface MetricTileProps {
  readonly icon: string;
  readonly label: string;
  readonly value: string;
  readonly tone?: StatusTone;
}

export function MetricTile({ icon, label, value, tone = "neutral" }: MetricTileProps) {
  return (
    <Card>
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-label-caps uppercase text-on-surface-variant">{label}</p>
          <p className="mt-2 text-telemetry-data text-secondary">{value}</p>
        </div>
        <span className="material-symbols-outlined rounded bg-primary/10 p-2 text-primary">{icon}</span>
      </div>
      <div className="mt-3">
        <StatusPill tone={tone}>Admin console</StatusPill>
      </div>
    </Card>
  );
}
