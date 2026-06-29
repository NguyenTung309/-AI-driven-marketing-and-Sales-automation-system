import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link, useParams } from "react-router-dom";
import { AppShell } from "@/shared/layout/AppShell";
import { Card } from "@/shared/ui/Card";
import { StatusPill, type StatusTone } from "@/shared/ui/StatusPill";
import { operationalPhaseLabel, toSafeOperationalText } from "@/shared/utils/userText";
import { TaskResultDetails } from "./TaskResultDetails";
import {
  getOrchestrationPlan,
  getOrchestrationTrace,
  type OrchestrationStatus,
  type OrchestrationTaskDto,
  type OrchestrationTraceDto,
} from "@/shared/api/orchestration";

const ACTIVE_STATUSES = new Set<OrchestrationStatus>(["draft", "pending_approval", "running", "paused"]);
const POLL_INTERVAL_MS = 3_000;

function statusTone(status: OrchestrationStatus): StatusTone {
  if (status === "completed") return "success";
  if (status === "failed" || status === "cancelled") return "error";
  if (status === "running" || status === "paused" || status === "pending_approval") return "warning";
  return "neutral";
}

function statusLabel(status: OrchestrationStatus): string {
  const map: Record<string, string> = {
    draft: "Nháp",
    pending_approval: "Chờ phê duyệt",
    running: "Đang chạy",
    paused: "Tạm dừng",
    completed: "Hoàn tất",
    failed: "Thất bại",
    cancelled: "Đã hủy",
  };
  return map[status] ?? status;
}

function taskTone(status: string): StatusTone {
  if (status === "completed") return "success";
  if (status === "failed") return "error";
  if (status === "skipped") return "neutral";
  return "warning";
}

// Order tasks root→leaf by dependency depth for an indented DAG (same logic the dashboard panel uses).
function tasksByDepth(tasks: readonly OrchestrationTaskDto[]): readonly { task: OrchestrationTaskDto; depth: number }[] {
  const depthById = new Map<string, number>();
  const byId = new Map(tasks.map((t) => [t.id, t]));
  const resolve = (id: string): number => {
    const cached = depthById.get(id);
    if (cached !== undefined) return cached;
    const task = byId.get(id);
    if (!task || task.dependsOn.length === 0) {
      depthById.set(id, 0);
      return 0;
    }
    const d = 1 + Math.max(...task.dependsOn.map(resolve));
    depthById.set(id, d);
    return d;
  };
  return tasks
    .map((task) => ({ task, depth: resolve(task.id) }))
    .sort((a, b) => a.depth - b.depth || a.task.id.localeCompare(b.task.id));
}

export default function AgentRunDetailPage() {
  const { sessionId = "" } = useParams<{ sessionId: string }>();

  const sessionQuery = useQuery({
    queryKey: ["orchestration", "session", sessionId],
    queryFn: () => getOrchestrationPlan(sessionId),
    enabled: Boolean(sessionId),
    refetchInterval: (query) =>
      query.state.data && ACTIVE_STATUSES.has(query.state.data.status) ? POLL_INTERVAL_MS : false,
  });
  const session = sessionQuery.data ?? null;

  const traceQuery = useQuery({
    queryKey: ["orchestration", "trace", sessionId],
    queryFn: () => getOrchestrationTrace(sessionId),
    enabled: Boolean(sessionId),
    refetchInterval: () => (session && ACTIVE_STATUSES.has(session.status) ? POLL_INTERVAL_MS : false),
  });
  const traceItems = traceQuery.data ?? [];

  const toolTracesByTask = useMemo(() => {
    const map = new Map<string, OrchestrationTraceDto[]>();
    for (const trace of traceItems) {
      if (!trace.phase?.toLowerCase().startsWith("tool")) continue;
      const list = map.get(trace.taskId) ?? [];
      list.push(trace);
      map.set(trace.taskId, list);
    }
    return map;
  }, [traceItems]);

  return (
    <AppShell title="Chi tiết phiên điều phối">
      <div className="mb-stack-lg flex flex-col gap-2">
        <Link className="text-label-sm text-primary hover:underline" to="/agents">
          ← Quay lại Giám sát Agent
        </Link>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-display-lg font-black text-on-surface">Chi tiết phiên</h1>
          {session && <StatusPill tone={statusTone(session.status)}>{statusLabel(session.status)}</StatusPill>}
          {traceQuery.isFetching && <span className="text-label-sm text-on-surface-variant">đang cập nhật...</span>}
        </div>
        {session && <p className="text-body-lg text-on-surface-variant">Mục tiêu: {session.goal}</p>}
      </div>

      {sessionQuery.isLoading ? (
        <Card>Đang tải phiên...</Card>
      ) : sessionQuery.isError || !session ? (
        <Card>Không tải được phiên này. Có thể phiên không tồn tại hoặc bạn không có quyền.</Card>
      ) : (
        <section className="grid grid-cols-1 gap-gutter 2xl:grid-cols-[minmax(0,1fr)_380px]">
          <Card className="flex flex-col gap-3">
            <h2 className="text-headline-sm font-bold text-secondary">Các bước xử lý</h2>
            <ul className="flex flex-col gap-2">
              {tasksByDepth(session.tasks).map(({ task, depth }) => (
                <li
                  key={task.id}
                  className="rounded-lg border border-outline p-3"
                  style={{ marginLeft: `${depth * 20}px` }}
                >
                  <div className="flex flex-wrap items-center gap-2">
                    {depth > 0 && <span className="text-label-sm text-on-surface-variant" aria-hidden>↳</span>}
                    <span className="font-mono text-mono-status text-on-surface-variant">{task.agent}</span>
                    <StatusPill tone={taskTone(task.status)}>{task.status}</StatusPill>
                  </div>
                  <p className="mt-1 text-body-md text-on-surface">{task.description}</p>
                  {task.error && <p className="text-label-sm text-error">{task.error}</p>}
                  <TaskResultDetails task={task} toolTraces={toolTracesByTask.get(task.id) ?? []} />
                </li>
              ))}
            </ul>
          </Card>

          <aside>
            <Card className="flex flex-col gap-2">
              <h2 className="text-headline-sm font-bold text-secondary">Nhật ký phiên</h2>
              {traceItems.length > 0 ? (
                <ul className="flex max-h-[70vh] flex-col gap-1 overflow-y-auto">
                  {traceItems.map((item, index) => (
                    <li key={`${item.taskId}-${item.phase}-${index}`} className="text-label-sm text-on-surface-variant">
                      <span className="font-mono">[{operationalPhaseLabel(item.phase)}]</span>{" "}
                      {item.agentName || item.taskId || "session"} · {toSafeOperationalText(item.message)}
                    </li>
                  ))}
                </ul>
              ) : (
                <p className="text-label-sm text-on-surface-variant">Chưa có nhật ký.</p>
              )}
            </Card>
          </aside>
        </section>
      )}
    </AppShell>
  );
}
