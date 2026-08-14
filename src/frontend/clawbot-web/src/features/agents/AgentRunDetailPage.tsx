import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link, useParams } from "react-router-dom";
import { AppShell } from "@/shared/layout/AppShell";
import { Alert } from "@/shared/ui/Alert";
import { Button } from "@/shared/ui/Button";
import { Card } from "@/shared/ui/Card";
import { StatusPill } from "@/shared/ui/StatusPill";
import { useAuthStore } from "@/shared/auth/authStore";
import {
  formatOperationalTraceMessage,
  operationalPhaseLabel,
  toSafeCsvCell,
  toUserFriendlyOrchestrationError,
} from "@/shared/utils/userText";
import { TaskResultDetails } from "./TaskResultDetails";
import { useOrchestrationRealtime } from "./useOrchestrationRealtime";
import { useRunControls } from "./useRunControls";
import { statusLabel, statusTone, taskStatusLabel, taskTone, tasksByDepth } from "./orchestrationStatus";
import {
  getOrchestrationV2Run,
  type OrchestrationV2RunDetail,
  type OrchestrationV2Status,
  type OrchestrationV2Trace,
} from "@/shared/api/orchestrationV2";

function downloadFile(name: string, mime: string, content: string) {
  const blob = new Blob([content], { type: mime });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = name;
  link.click();
  URL.revokeObjectURL(url);
}

// BOM để Excel nhận đúng UTF-8 tiếng Việt.
function exportRunCsv(session: OrchestrationV2RunDetail) {
  const rows = [
    ["thoi_gian", "agent", "giai_doan", "noi_dung", "task"],
    ...session.traces.map((trace) => [
      trace.occurredAt,
      trace.agentName,
      operationalPhaseLabel(trace.phase),
      formatOperationalTraceMessage(trace.phase, trace.message),
      trace.taskId,
    ]),
  ];
  const csv = "﻿" + rows
    .map((row) => row.map(toSafeCsvCell).join(","))
    .join("\n");
  downloadFile(`phien-${session.sessionId}.csv`, "text/csv;charset=utf-8", csv);
}

const ACTIVE_STATUSES = new Set<OrchestrationV2Status>([
  "draft",
  "pending_approval",
  "running",
  "pause_requested",
  "paused",
  "cancelling",
  "failing",
]);
const POLL_INTERVAL_MS = 3_000;

export default function AgentRunDetailPage() {
  const { sessionId = "" } = useParams<{ sessionId: string }>();
  const permissions = useAuthStore((s) => s.permissions);
  const canApprove = permissions.includes("orchestration:approve");
  const canManage = permissions.includes("orchestration:manage");
  const controls = useRunControls(sessionId || null);

  const live = useOrchestrationRealtime(true) === "connected";
  const sessionQuery = useQuery({
    queryKey: ["orchestration", "session", sessionId],
    queryFn: () => getOrchestrationV2Run(sessionId),
    enabled: Boolean(sessionId),
    refetchInterval: (query) =>
      query.state.data && ACTIVE_STATUSES.has(query.state.data.status) ? (live ? 30_000 : POLL_INTERVAL_MS) : false,
  });
  const session = sessionQuery.data ?? null;

  // Traces are embedded in the run-detail response, so no separate polling query is needed.
  const traceItems = useMemo(() => session?.traces ?? [], [session?.traces]);

  const toolTracesByTask = useMemo(() => {
    const map = new Map<string, OrchestrationV2Trace[]>();
    for (const trace of traceItems) {
      const phase = trace.phase?.toLowerCase();
      if (!phase?.startsWith("tool") || phase === "tool_skipped") continue;
      const list = map.get(trace.taskId) ?? [];
      list.push(trace);
      map.set(trace.taskId, list);
    }
    return map;
  }, [traceItems]);

  // Bước có kết quả do người dùng sửa tay khi phiên tạm dừng.
  const editedTaskIds = useMemo(
    () => new Set(traceItems.filter((trace) => trace.phase === "task_edited").map((trace) => trace.taskId)),
    [traceItems],
  );

  return (
    <AppShell title="Chi tiết phiên điều phối">
      <div className="mb-stack-lg flex flex-col gap-2">
        <Link className="text-label-sm text-primary hover:underline" to="/agents">
          ← Quay lại Giám sát Agent
        </Link>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-display-lg font-black text-on-surface">Chi tiết phiên</h1>
          {session && <StatusPill tone={statusTone(session.status)}>{statusLabel(session.status)}</StatusPill>}
          {sessionQuery.isFetching && <span className="text-label-sm text-on-surface-variant">đang cập nhật...</span>}
        </div>
        {session && (
          <div className="flex flex-wrap items-center gap-3">
            <p className="text-body-lg text-on-surface-variant">Mục tiêu: {session.goal}</p>
            {session.actualCostUsd > 0 ? (
              <StatusPill tone="neutral">Chi phí thực: ${session.actualCostUsd.toFixed(4)}</StatusPill>
            ) : null}
          </div>
        )}

        {session && (
          <div className="flex flex-wrap items-center gap-2">
            {session.status === "pending_approval" && (
              <Button
                disabled={!canApprove || controls.busy}
                onClick={() => controls.approve.mutate({ etag: session.etag })}
                title={!canApprove ? "Cần quyền orchestration:approve" : undefined}
              >
                Phê duyệt & chạy
              </Button>
            )}
            {session.status === "running" && (
              <Button
                disabled={!canManage || controls.busy}
                onClick={() => controls.control.mutate({ action: "pause", etag: session.etag })}
                title={!canManage ? "Cần quyền orchestration:manage" : undefined}
                variant="outline"
              >
                Tạm dừng
              </Button>
            )}
            {session.status === "paused" && (
              <Button
                disabled={!canManage || controls.busy}
                onClick={() => controls.control.mutate({ action: "resume", etag: session.etag })}
                title={!canManage ? "Cần quyền orchestration:manage" : undefined}
                variant="outline"
              >
                Tiếp tục
              </Button>
            )}
            {(session.status === "running" || session.status === "paused") && (
              <Button
                disabled={!canManage || controls.busy}
                onClick={() => {
                  if (window.confirm("Hủy phiên này? Các task đang chạy sẽ dừng lại.")) {
                    controls.control.mutate({ action: "cancel", etag: session.etag });
                  }
                }}
                title={!canManage ? "Cần quyền orchestration:manage" : undefined}
                variant="ghost"
              >
                Hủy phiên
              </Button>
            )}
            <Button
              onClick={() => downloadFile(`phien-${session.sessionId}.json`, "application/json", JSON.stringify(session, null, 2))}
              variant="outline"
            >
              Xuất JSON
            </Button>
            <Button disabled={!traceItems.length} onClick={() => exportRunCsv(session)} variant="outline">
              Xuất CSV nhật ký
            </Button>
          </div>
        )}
        {controls.error ? (
          <Alert tone="error">
            Thao tác thất bại: {controls.error instanceof Error ? controls.error.message : "lỗi không xác định"}
          </Alert>
        ) : null}
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
                    <StatusPill tone={taskTone(task.status)}>{taskStatusLabel(task.status)}</StatusPill>
                  </div>
                  <p className="mt-1 text-body-md text-on-surface">{task.description}</p>
                  {task.error && (
                    <p className="text-label-sm text-error">
                      {toUserFriendlyOrchestrationError(task.error) ?? task.error}
                    </p>
                  )}
                  <TaskResultDetails
                    editedByUser={editedTaskIds.has(task.id)}
                    task={task}
                    toolTraces={toolTracesByTask.get(task.id) ?? []}
                  />
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
                      {item.agentName || item.taskId || "session"} · {formatOperationalTraceMessage(item.phase, item.message)}
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
