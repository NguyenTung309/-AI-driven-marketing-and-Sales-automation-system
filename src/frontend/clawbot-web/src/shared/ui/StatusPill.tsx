import type { ReactNode } from "react";

export type StatusTone = "success" | "warning" | "error" | "neutral";

export interface StatusPillProps {
  readonly tone?: StatusTone;
  readonly children: ReactNode;
}

const TONE: Record<StatusTone, string> = {
  success: "bg-success/10 text-success",
  warning: "bg-warning/10 text-warning",
  error: "bg-error/10 text-error",
  neutral: "bg-surface-variant text-on-surface-variant",
};

// Pill-shaped status tag: light fill + dark text + leading dot (e.g. "Đang chạy").
export function StatusPill({ tone = "neutral", children }: StatusPillProps) {
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 font-mono text-mono-status ${TONE[tone]}`}>
      <span className="size-1.5 rounded-full bg-current" />
      {children}
    </span>
  );
}
