import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSearchParams } from "react-router-dom";
import { Alert } from "@/shared/ui/Alert";
import { Button } from "@/shared/ui/Button";
import { Card } from "@/shared/ui/Card";
import { Modal } from "@/shared/ui/Modal";
import { StatusPill, type StatusTone } from "@/shared/ui/StatusPill";
import { useAuthStore } from "@/shared/auth/authStore";
import { toSafeOperationalText, operationalPhaseLabel } from "@/shared/utils/userText";
import { TaskResultDetails } from "./TaskResultDetails";
import {
  approveOrchestration,
  archiveOrchestrationRun,
  controlOrchestration,
  controlOrchestrationRun,
  getOrchestrationPlan,
  getOrchestrationTrace,
  listOrchestrationRuns,
  submitOrchestration,
  updateOrchestrationPlan,
  type OrchestrationControlAction,
  type OrchestrationRunListItem,
  type OrchestrationSessionDto,
  type OrchestrationStatus,
  type OrchestrationTaskDto,
  type OrchestrationTraceDto,
} from "@/shared/api/orchestration";

const ACTIVE_STATUSES = new Set<OrchestrationStatus>(["draft", "pending_approval", "running", "paused"]);
const POLL_INTERVAL_MS = 3_000;

function statusTone(status: OrchestrationStatus): StatusTone {
  switch (status) {
    case "completed":
      return "success";
    case "failed":
    case "cancelled":
      return "error";
    case "running":
    case "paused":
    case "pending_approval":
      return "warning";
    default:
      return "neutral";
  }
}

function statusLabel(status: OrchestrationStatus): string {
  switch (status) {
    case "draft":
      return "Nháp";
    case "pending_approval":
      return "Chờ phê duyệt";
    case "running":
      return "Đang chạy";
    case "paused":
      return "Tạm dừng";
    case "completed":
      return "Hoàn tất";
    case "failed":
      return "Thất bại";
    case "cancelled":
      return "Đã hủy";
    default:
      return status;
  }
}

function taskTone(status: string): StatusTone {
  switch (status) {
    case "completed":
      return "success";
    case "failed":
      return "error";
    case "skipped":
      return "neutral";
    default:
      return "warning";
  }
}

// SPEC-16 P3-2: order tasks by dependency depth (topological) so the DAG reads root→leaf, and expose each
// task's depth for indentation — a lightweight graph visualization without a layout engine.
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
    const d = 1 + Math.max(...task.dependsOn.map((dep) => resolve(dep)));
    depthById.set(id, d);
    return d;
  };
  return tasks
    .map((task) => ({ task, depth: resolve(task.id) }))
    .sort((a, b) => a.depth - b.depth || a.task.id.localeCompare(b.task.id));
}

function errorMessage(error: unknown): string {
  if (error instanceof Error) return error.message;
  return "Đã xảy ra lỗi không xác định.";
}

export function OrchestrationPanel() {
  const permissions = useAuthStore((s) => s.permissions);
  const can = (code: string) => permissions.includes(code);
  const queryClient = useQueryClient();

  const [searchParams, setSearchParams] = useSearchParams();
  const sessionId = searchParams.get("sessionId");

  const [goal, setGoal] = useState("");
  // Editable plan draft. `planSource` tracks which fetched plan the draft was seeded from, so we can
  // re-seed during render when a new plan arrives (React's "adjust state on prop change" pattern)
  // without an effect-driven cascading render.
  const [draft, setDraft] = useState<{ source: string; text: string } | null>(null);
  // SPEC-16 P3-1: raw-JSON plan edit is behind an "advanced" toggle so the structured DAG is the primary view.
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [showRecentRuns, setShowRecentRuns] = useState(false);

  const setSessionId = (next: string | null): void => {
    setSearchParams(
      (params) => {
        if (next) params.set("sessionId", next);
        else params.delete("sessionId");
        return params;
      },
      { replace: true },
    );
  };

  // Durable session state: read by sessionId from the URL so it survives route changes and F5.
  const sessionQuery = useQuery({
    queryKey: ["orchestration", "session", sessionId],
    queryFn: () => getOrchestrationPlan(sessionId!),
    enabled: Boolean(sessionId),
    refetchInterval: (query) =>
      query.state.data && ACTIVE_STATUSES.has(query.state.data.status) ? POLL_INTERVAL_MS : false,
  });
  const session = sessionQuery.data ?? null;

  // SPEC-16 P3-6: recent/in-flight run list (URL-independent) so the user can switch runs without a sessionId in hand.
  const runsQuery = useQuery({
    queryKey: ["orchestration", "runs"],
    queryFn: () => listOrchestrationRuns(false),
    refetchInterval: POLL_INTERVAL_MS,
  });

  const traceQuery = useQuery({
    queryKey: ["orchestration", "trace", sessionId],
    queryFn: () => getOrchestrationTrace(sessionId!),
    enabled: Boolean(sessionId),
    refetchInterval: () => (session && ACTIVE_STATUSES.has(session.status) ? POLL_INTERVAL_MS : false),
  });
  const traceItems = traceQuery.data ?? [];

  // Group tool-action traces (tool_executed/tool_failed/tool_blocked/…) by the task that produced them, so each
  // agent step can show exactly what it did on the system.
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

  // Re-seed the editable draft when the fetched plan changes (keyed by sessionId + planJson).
  const planSource = session ? `${session.sessionId}:${session.planJson}` : null;
  if (planSource && draft?.source !== planSource) {
    setDraft({ source: planSource, text: session!.planJson });
  }
  const planDraft = draft?.text ?? "";
  const setPlanDraft = (text: string): void =>
    setDraft((current) => ({ source: current?.source ?? planSource ?? "", text }));

  const onSession = (next: OrchestrationSessionDto): void => {
    queryClient.setQueryData(["orchestration", "session", next.sessionId], next);
    setSessionId(next.sessionId);
  };

  const submit = useMutation({
    mutationFn: () => submitOrchestration(goal.trim()),
    onSuccess: onSession,
  });
  const approve = useMutation({
    mutationFn: (vars: { sessionId: string; etag: string }) => approveOrchestration(vars.sessionId, vars.etag),
    onSuccess: onSession,
  });
  const control = useMutation({
    mutationFn: (vars: { sessionId: string; action: OrchestrationControlAction; etag: string }) =>
      controlOrchestration(vars.sessionId, vars.action, vars.etag),
    onSuccess: onSession,
  });
  const updatePlan = useMutation({
    mutationFn: (vars: { sessionId: string; planJson: string; etag: string }) =>
      updateOrchestrationPlan(vars.sessionId, vars.planJson, vars.etag),
    onSuccess: onSession,
  });
  const archiveRun = useMutation({
    mutationFn: archiveOrchestrationRun,
    onSuccess: async (_data, archivedSessionId) => {
      if (sessionId === archivedSessionId) setSessionId(null);
      await queryClient.invalidateQueries({ queryKey: ["orchestration", "runs"] });
    },
  });
  const cancelRun = useMutation({
    mutationFn: (runSessionId: string) => controlOrchestrationRun(runSessionId, "cancel"),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["orchestration", "runs"] });
    },
  });

  const busy = submit.isPending || approve.isPending || control.isPending || updatePlan.isPending || archiveRun.isPending || cancelRun.isPending;
  const activeError = submit.error ?? approve.error ?? control.error ?? updatePlan.error ?? archiveRun.error ?? cancelRun.error ?? sessionQuery.error;
  const canRun = can("orchestration:run");
  const canApprove = can("orchestration:approve");
  const canManage = can("orchestration:manage");
  const canEditPlan = Boolean(session && canRun && (session.status === "draft" || session.status === "pending_approval"));
  const isPlanning = submit.isPending || (session !== null && session.status === "running" && session.tasks.every((t) => t.status === "pending"));

  return (
    <Card className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-title-md text-on-surface">Điều phối tác nhân động</h2>
          <p className="text-label-sm text-on-surface-variant">
            Nhập mục tiêu, hệ thống lập kế hoạch nhiều tác nhân và thực thi theo DAG.
          </p>
        </div>
        {session && <StatusPill tone={statusTone(session.status)}>{statusLabel(session.status)}</StatusPill>}
      </div>

      <div className="flex flex-col gap-2">
        <textarea
          value={goal}
          onChange={(e) => setGoal(e.target.value)}
          rows={3}
          placeholder="Ví dụ: Lên chiến dịch ra mắt khóa HSK4 và soạn nội dung đa kênh."
          className="w-full rounded-lg border border-outline bg-surface-container-lowest p-3 text-body-md text-on-surface focus:border-primary focus:outline-none"
          disabled={!canRun || busy}
        />
        <div className="flex items-center gap-2">
          <Button onClick={() => submit.mutate()} disabled={!canRun || busy || goal.trim().length === 0}>
            Gửi mục tiêu
          </Button>
          {session && (
            <Button variant="outline" onClick={() => void sessionQuery.refetch()} disabled={busy || sessionQuery.isFetching}>
              Làm mới
            </Button>
          )}
          {session && (
            <Button
              variant="ghost"
              onClick={() => {
                setGoal("");
                setSessionId(null);
              }}
              disabled={busy}
            >
              Mục tiêu mới
            </Button>
          )}
        </div>
        {!canRun && (
          <p className="text-label-sm text-on-surface-variant">Bạn không có quyền chạy điều phối (orchestration:run).</p>
        )}
      </div>

      {/* SPEC-16 P3-6: recent/in-flight runs — open as a modal so the main orchestration surface stays focused. */}
      {runsQuery.data && runsQuery.data.length > 0 && (
        <div className="flex items-center justify-between rounded-lg border border-outline p-3">
          <div>
            <h3 className="text-label-md text-on-surface">Phiên gần đây</h3>
            <p className="text-label-sm text-on-surface-variant">{runsQuery.data.length} phiên có thể mở lại.</p>
          </div>
          <Button variant="outline" onClick={() => setShowRecentRuns(true)} disabled={busy}>
            Xem phiên
          </Button>
        </div>
      )}

      {isPlanning && (
        <Alert tone="info">
          <span className="inline-flex items-center gap-2">
            <span aria-hidden="true" className="material-symbols-outlined animate-spin text-[18px]">progress_activity</span>
            Agent đang lập kế hoạch... Bạn có thể rời trang, tiến trình vẫn được lưu.
          </span>
        </Alert>
      )}

      {activeError && <Alert tone="error">{errorMessage(activeError)}</Alert>}

      {session?.status === "failed" && (
        <Alert tone="error">
          Lập kế hoạch hoặc thực thi thất bại. Xem nhật ký bên dưới để biết bước bị lỗi, chỉnh lại mục tiêu rồi gửi lại.
        </Alert>
      )}

      {session?.costBlocked && (
        <Alert tone="warning">
          Vượt ngưỡng chi phí ({session.costReason ?? "cost_cap"}). Kế hoạch chuyển sang chờ phê duyệt.
        </Alert>
      )}

      <Modal open={showRecentRuns} onClose={() => setShowRecentRuns(false)} title="Phiên gần đây">
        {runsQuery.isFetching && <p className="text-label-sm text-on-surface-variant">Đang cập nhật...</p>}
        {runsQuery.data && runsQuery.data.length > 0 ? (
          <ul className="flex max-h-[60vh] flex-col gap-2 overflow-y-auto pr-1">
            {runsQuery.data.map((run: OrchestrationRunListItem) => {
              const canCancelRun = run.status === "running" || run.status === "paused";
              const canArchiveRun = run.status === "completed" || run.status === "failed" || run.status === "cancelled";
              return (
                <li className="flex items-center gap-2 rounded-lg border border-outline px-3 py-2" key={run.sessionId}>
                  <button
                    type="button"
                    className="flex min-w-0 flex-1 items-center gap-3 text-left hover:text-primary"
                    onClick={() => {
                      setSessionId(run.sessionId);
                      setShowRecentRuns(false);
                    }}
                    disabled={busy}
                  >
                    <StatusPill tone={statusTone(run.status)}>{statusLabel(run.status)}</StatusPill>
                    <span className="min-w-0 flex-1 truncate text-body-sm text-on-surface">{run.goal || "(không có mục tiêu)"}</span>
                    <span className="shrink-0 text-label-sm text-on-surface-variant">
                      {new Date(run.startedAt).toLocaleString()}
                    </span>
                  </button>
                  {canCancelRun ? (
                    <Button
                      variant="ghost"
                      onClick={() => cancelRun.mutate(run.sessionId)}
                      disabled={!canManage || busy}
                    >
                      Hủy
                    </Button>
                  ) : null}
                  {canArchiveRun ? (
                    <Button
                      variant="ghost"
                      onClick={() => archiveRun.mutate(run.sessionId)}
                      disabled={!canManage || busy}
                    >
                      Ẩn
                    </Button>
                  ) : null}
                </li>
              );
            })}
          </ul>
        ) : (
          <p className="text-label-sm text-on-surface-variant">Chưa có phiên nào.</p>
        )}
      </Modal>

      {session && (
        <div className="flex flex-col gap-3">
          <div className="flex flex-wrap items-center gap-2 text-label-sm text-on-surface-variant">
            <span>Mục tiêu: {session.goal}</span>
            {session.requiresApproval && <StatusPill tone="warning">Cần phê duyệt</StatusPill>}
            {session.replanCount > 0 && <span>Lập lại kế hoạch: {session.replanCount}</span>}
          </div>

          {session.tasks.length > 0 && (
            <button
              type="button"
              className="self-start text-label-sm text-primary hover:underline"
              onClick={() => setShowAdvanced(true)}
            >
              ▸ Chỉnh sửa JSON nâng cao
            </button>
          )}

          {/* Advanced raw-JSON plan editor — in a wide modal so the full plan is readable/editable. */}
          <Modal
            open={showAdvanced}
            onClose={() => setShowAdvanced(false)}
            title="Chỉnh sửa kế hoạch JSON"
            maxWidthClass="max-w-3xl"
            footer={
              <>
                <Button variant="ghost" onClick={() => setShowAdvanced(false)} disabled={busy}>
                  Đóng
                </Button>
                <Button
                  onClick={() => {
                    if (!session) return;
                    updatePlan.mutate(
                      { sessionId: session.sessionId, planJson: planDraft, etag: session.etag },
                      { onSuccess: () => setShowAdvanced(false) },
                    );
                  }}
                  disabled={!canEditPlan || busy || planDraft.trim().length === 0}
                >
                  Lưu kế hoạch
                </Button>
              </>
            }
          >
            {!canEditPlan && (
              <p className="text-label-sm text-on-surface-variant">
                Chỉ chỉnh sửa được khi phiên ở trạng thái Nháp hoặc Chờ phê duyệt.
              </p>
            )}
            <label className="text-label-sm text-on-surface-variant" htmlFor="orchestration-plan-json">
              Kế hoạch JSON có thể chỉnh sửa
            </label>
            <textarea
              id="orchestration-plan-json"
              value={planDraft}
              onChange={(e) => setPlanDraft(e.target.value)}
              rows={20}
              className="mt-2 max-h-[60vh] w-full resize-y rounded-lg border border-outline bg-surface-container-lowest p-3 font-mono text-mono-status text-on-surface focus:border-primary focus:outline-none"
              disabled={!canEditPlan || busy}
              spellCheck={false}
            />
          </Modal>

          <ul className="flex flex-col gap-2">
            {tasksByDepth(session.tasks).map(({ task, depth }) => (
              <li
                key={task.id}
                className="flex items-start justify-between gap-3 rounded-lg border border-outline p-3"
                style={{ marginLeft: `${depth * 20}px` }}
              >
                <div className="min-w-0">
                  <div className="flex items-center gap-2">
                    {depth > 0 && <span className="text-label-sm text-on-surface-variant" aria-hidden>↳</span>}
                    <span className="font-mono text-mono-status text-on-surface-variant">{task.agent}</span>
                    <StatusPill tone={taskTone(task.status)}>{task.status}</StatusPill>
                    {task.useCount && task.useCount > 1 ? (
                      <span className="rounded-full bg-surface-container-high px-2 text-label-sm text-on-surface-variant" title={`Agent ${task.agent} được dùng ${task.useCount} lần trong phiên`}>
                        ×{task.useCount}
                      </span>
                    ) : null}
                    {task.currentTaskId === task.id ? (
                      <span className="rounded-full bg-primary/10 px-2 text-label-sm text-primary">đang chạy</span>
                    ) : null}
                  </div>
                  <p className="text-body-md text-on-surface">{task.description}</p>
                  {task.error && <p className="text-label-sm text-error">{task.error}</p>}
                  <TaskResultDetails task={task} toolTraces={toolTracesByTask.get(task.id) ?? []} />
                </div>
              </li>
            ))}
          </ul>

          {sessionId && (
            <a
              className="self-start text-label-sm text-primary hover:underline"
              href={`/agents/runs/${encodeURIComponent(sessionId)}`}
            >
              Mở chi tiết phiên ↗
            </a>
          )}

          <div className="rounded-lg border border-outline p-3">
            <div className="flex items-center justify-between">
              <h3 className="text-label-md text-on-surface">Nhật ký điều phối</h3>
              {traceQuery.isFetching && <span className="text-label-sm text-on-surface-variant">đang cập nhật...</span>}
            </div>
            {traceItems.length > 0 ? (
              <ul className="mt-2 flex flex-col gap-1">
                {traceItems.map((item, index) => (
                  <li key={`${item.taskId}-${item.phase}-${index}`} className="text-label-sm text-on-surface-variant">
                    <span className="font-mono">{operationalPhaseLabel(item.phase)}</span> ·{" "}
                    {item.agentName || item.taskId || "session"} · {toSafeOperationalText(item.message)}
                  </li>
                ))}
              </ul>
            ) : (
              <p className="mt-2 text-label-sm text-on-surface-variant">Chưa có nhật ký. Tiến trình sẽ tự cập nhật.</p>
            )}
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {session.status === "pending_approval" && (
              <Button onClick={() => approve.mutate({ sessionId: session.sessionId, etag: session.etag })} disabled={!canApprove || busy}>
                Phê duyệt &amp; chạy
              </Button>
            )}
            {session.status === "running" && (
              <Button
                variant="outline"
                onClick={() => control.mutate({ sessionId: session.sessionId, action: "pause", etag: session.etag })}
                disabled={!canManage || busy}
              >
                Tạm dừng
              </Button>
            )}
            {session.status === "paused" && (
              <Button
                variant="outline"
                onClick={() => control.mutate({ sessionId: session.sessionId, action: "resume", etag: session.etag })}
                disabled={!canManage || busy}
              >
                Tiếp tục
              </Button>
            )}
            {(session.status === "running" || session.status === "paused") && (
              <Button
                variant="ghost"
                onClick={() => control.mutate({ sessionId: session.sessionId, action: "cancel", etag: session.etag })}
                disabled={!canManage || busy}
              >
                Hủy
              </Button>
            )}
          </div>
        </div>
      )}
    </Card>
  );
}
