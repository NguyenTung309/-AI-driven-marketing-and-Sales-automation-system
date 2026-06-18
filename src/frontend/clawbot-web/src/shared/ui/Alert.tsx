import type { ReactNode } from "react";

export type AlertTone = "info" | "success" | "warning" | "error";

export interface AlertProps {
  readonly tone?: AlertTone;
  readonly icon?: string; // override Material Symbols name
  readonly children: ReactNode;
}

const TONE: Record<AlertTone, { readonly box: string; readonly icon: string }> = {
  info: { box: "bg-primary/5 border-primary/20", icon: "text-primary" },
  success: { box: "bg-success/10 border-success/30", icon: "text-success" },
  warning: { box: "bg-warning/10 border-warning/30", icon: "text-warning" },
  error: { box: "bg-error/10 border-error/30", icon: "text-error" },
};

const DEFAULT_ICON: Record<AlertTone, string> = {
  info: "info",
  success: "check_circle",
  warning: "warning",
  error: "error",
};

export function Alert({ tone = "info", icon, children }: AlertProps) {
  const t = TONE[tone];
  return (
    <div className={`flex gap-3 p-4 rounded-lg border ${t.box}`}>
      <span aria-hidden="true" className={`material-symbols-outlined text-[20px] shrink-0 ${t.icon}`}>{icon ?? DEFAULT_ICON[tone]}</span>
      <div className="text-label-sm text-on-surface leading-relaxed">{children}</div>
    </div>
  );
}
