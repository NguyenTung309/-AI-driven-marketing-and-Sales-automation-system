import { Card, StatusPill, type StatusTone } from "@/shared/ui";

export function MetricTile({
  icon,
  label,
  value,
  tone = "neutral",
}: {
  readonly icon: string;
  readonly label: string;
  readonly value: string;
  readonly tone?: StatusTone;
}) {
  return (
    <Card>
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-label-caps uppercase text-on-surface-variant">{label}</p>
          <p className="mt-2 text-telemetry-data text-secondary">{value}</p>
        </div>
        <span aria-hidden="true" className="material-symbols-outlined rounded bg-primary/10 p-2 text-primary">{icon}</span>
      </div>
      <div className="mt-3">
        <StatusPill tone={tone}>Quản trị</StatusPill>
      </div>
    </Card>
  );
}

export function EmptyState({ children }: { readonly children: string }) {
  return (
    <div className="rounded-lg border border-dashed border-outline bg-surface p-6 text-center text-body-md text-on-surface-variant">
      {children}
    </div>
  );
}

export function TabButton({
  active,
  icon,
  label,
  onClick,
}: {
  readonly active: boolean;
  readonly icon: string;
  readonly label: string;
  readonly onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`inline-flex items-center gap-2 border-b-2 px-4 py-3 text-label-caps uppercase ${
        active ? "border-primary text-primary" : "border-transparent text-on-surface-variant hover:text-secondary"
      }`}
    >
      <span aria-hidden="true" className="material-symbols-outlined text-[18px]">{icon}</span>
      {label}
    </button>
  );
}

export function Field({
  label,
  children,
}: {
  readonly label: string;
  readonly children: React.ReactNode;
}) {
  return (
    <label className="block">
      <span className="mb-1 block text-label-sm font-semibold text-secondary">{label}</span>
      {children}
    </label>
  );
}
