import { useQuery } from "@tanstack/react-query";
import { Modal } from "@/shared/ui/Modal";
import { StatusPill } from "@/shared/ui/StatusPill";
import { statusLabel, statusTone, taskStatusLabel, taskTone } from "./orchestrationStatus";
import { getOrchestrationV2Run, type OrchestrationV2RunDetail, type OrchestrationV2TaskDto } from "@/shared/api/orchestrationV2";

function durationLabel(detail: OrchestrationV2RunDetail): string {
  if (!detail.finishedAt) return "—";
  const ms = new Date(detail.finishedAt).getTime() - new Date(detail.startedAt).getTime();
  if (!Number.isFinite(ms) || ms < 0) return "—";
  const seconds = Math.round(ms / 1000);
  return seconds < 60 ? `${seconds} giây` : `${Math.round(seconds / 60)} phút`;
}

function lastError(detail: OrchestrationV2RunDetail): string | null {
  const trace = [...detail.traces].reverse().find((item) => {
    const phase = item.phase.toLowerCase();
    return phase.includes("fail") || phase.includes("error");
  });
  return trace ? trace.message : null;
}

function TaskCell({ tasks }: { readonly tasks: readonly OrchestrationV2TaskDto[] }) {
  if (!tasks.length) return <span className="text-label-sm text-on-surface-variant">— không có task —</span>;
  return (
    <div className="flex flex-col gap-1">
      {tasks.map((task) => (
        <div className="flex flex-col gap-0.5" key={task.id}>
          <span className="flex items-center gap-2">
            <StatusPill tone={taskTone(task.status)}>{taskStatusLabel(task.status)}</StatusPill>
            <span className="truncate text-label-sm text-on-surface" title={task.description}>{task.description}</span>
          </span>
          {task.error ? <span className="text-label-sm text-error">{task.error}</span> : null}
        </div>
      ))}
    </div>
  );
}

// C3: so sánh 2 phiên cạnh nhau — align task theo agent, tô sáng khác biệt trạng thái.
// Thuần frontend trên 2 detail DTO đã có sẵn (dùng chung query cache với các trang khác).
export function RunCompareDialog({
  ids,
  onClose,
}: {
  readonly ids: readonly [string, string] | null;
  readonly onClose: () => void;
}) {
  const leftQuery = useQuery({
    queryKey: ["orchestration", "session", ids?.[0] ?? null],
    queryFn: () => getOrchestrationV2Run(ids![0]),
    enabled: Boolean(ids),
  });
  const rightQuery = useQuery({
    queryKey: ["orchestration", "session", ids?.[1] ?? null],
    queryFn: () => getOrchestrationV2Run(ids![1]),
    enabled: Boolean(ids),
  });

  const left = leftQuery.data ?? null;
  const right = rightQuery.data ?? null;
  const agents = left && right
    ? [...new Set([...left.tasks.map((task) => task.agent), ...right.tasks.map((task) => task.agent)])]
    : [];

  const renderHeader = (detail: OrchestrationV2RunDetail) => (
    <div className="flex flex-col gap-1">
      <div className="flex flex-wrap items-center gap-2">
        <StatusPill tone={statusTone(detail.status)}>{statusLabel(detail.status)}</StatusPill>
        <span className="text-label-sm text-on-surface-variant">{new Date(detail.startedAt).toLocaleString("vi-VN")}</span>
        <span className="text-label-sm text-on-surface-variant">· {durationLabel(detail)}</span>
        {detail.replanCount > 0 ? <span className="text-label-sm text-warning">· lập lại kế hoạch ×{detail.replanCount}</span> : null}
      </div>
      <p className="line-clamp-2 text-body-md text-on-surface" title={detail.goal}>{detail.goal}</p>
      {lastError(detail) ? <p className="line-clamp-2 text-label-sm text-error">{lastError(detail)}</p> : null}
    </div>
  );

  return (
    <Modal maxWidthClass="max-w-5xl" onClose={onClose} open={Boolean(ids)} title="So sánh hai phiên">
      {!left || !right ? (
        <p className="text-body-md text-on-surface-variant">Đang tải hai phiên...</p>
      ) : (
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-4">
            {renderHeader(left)}
            {renderHeader(right)}
          </div>

          <div className="overflow-x-auto rounded-lg border border-outline">
            <table className="w-full text-left text-body-md">
              <thead>
                <tr className="text-label-sm text-on-surface-variant">
                  <th className="w-36 px-3 py-2">Agent</th>
                  <th className="px-3 py-2">Phiên A</th>
                  <th className="px-3 py-2">Phiên B</th>
                </tr>
              </thead>
              <tbody>
                {agents.map((agent) => {
                  const leftTasks = left.tasks.filter((task) => task.agent === agent);
                  const rightTasks = right.tasks.filter((task) => task.agent === agent);
                  const differs =
                    leftTasks.map((task) => task.status).join(",") !== rightTasks.map((task) => task.status).join(",");
                  return (
                    <tr className={`border-t border-outline align-top ${differs ? "bg-warning/10" : ""}`} key={agent}>
                      <td className="px-3 py-3 font-mono text-mono-status text-secondary">{agent}</td>
                      <td className="px-3 py-3"><TaskCell tasks={leftTasks} /></td>
                      <td className="px-3 py-3"><TaskCell tasks={rightTasks} /></td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
          <p className="text-label-sm text-on-surface-variant">Hàng tô vàng = hai phiên cho kết quả khác nhau ở agent đó.</p>
        </div>
      )}
    </Modal>
  );
}
