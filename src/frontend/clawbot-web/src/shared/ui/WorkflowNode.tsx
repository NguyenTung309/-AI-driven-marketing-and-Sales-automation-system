import type { ReactNode } from "react";
import type { StatusTone } from "./StatusPill";

export interface WorkflowNodeProps {
  readonly title: string;
  readonly subtitle?: string; // agent type
  readonly status?: StatusTone;
  readonly children?: ReactNode; // port rows / body
}

const DOT: Record<StatusTone, string> = {
  success: "bg-success",
  warning: "bg-warning",
  error: "bg-error",
  neutral: "bg-on-surface-variant/40",
};

// Node for the dot-grid workflow canvas: header (agent type + status dot), 8px radius.
export function WorkflowNode({ title, subtitle, status = "neutral", children }: WorkflowNodeProps) {
  return (
    <div className="bg-surface-container-lowest border border-outline rounded-lg shadow-sm w-56">
      <div className="flex items-center justify-between px-3 py-2 border-b border-surface-variant">
        <div>
          <p className="text-body-md font-semibold leading-tight">{title}</p>
          {subtitle ? <p className="text-label-caps uppercase text-on-surface-variant">{subtitle}</p> : null}
        </div>
        <span className={`size-2 rounded-full ${DOT[status]}`} />
      </div>
      {children ? (
        <div className="px-3 py-2 font-mono text-mono-status text-on-surface-variant space-y-1">{children}</div>
      ) : null}
    </div>
  );
}
