import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Alert } from "@/shared/ui/Alert";
import { Button } from "@/shared/ui/Button";
import { Card } from "@/shared/ui/Card";
import { StatusPill, type StatusTone } from "@/shared/ui/StatusPill";
import { useAuthStore } from "@/shared/auth/authStore";
import {
  approveOrchestration,
  controlOrchestration,
  getOrchestrationPlan,
  getOrchestrationTrace,
  submitOrchestration,
  updateOrchestrationPlan,
  type OrchestrationControlAction,
  type OrchestrationSessionDto,
  type OrchestrationStatus,
  type OrchestrationTraceDto,
} from "@/shared/api/orchestration";

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

function errorMessage(error: unknown): string {
  if (error instanceof Error) return error.message;
  return "Đã xảy ra lỗi không xác định.";
}

export function OrchestrationPanel() {
  const permissions = useAuthStore((s) => s.permissions);
  const can = (code: string) => permissions.includes(code);

  const [goal, setGoal] = useState("");
  const [session, setSession] = useState<OrchestrationSessionDto | null>(null);
  const [planDraft, setPlanDraft] = useState("");
  const [traceItems, setTraceItems] = useState<readonly OrchestrationTraceDto[]>([]);

  const applySession = (nextSession: OrchestrationSessionDto): void => {
    setSession(nextSession);
    setPlanDraft(nextSession.planJson);
  };

  const submit = useMutation({
    mutationFn: () => submitOrchestration(goal.trim()),
    onSuccess: applySession,
  });
  const approve = useMutation({
    mutationFn: (vars: { sessionId: string; etag: string }) => approveOrchestration(vars.sessionId, vars.etag),
    onSuccess: applySession,
  });
  const control = useMutation({
    mutationFn: (vars: { sessionId: string; action: OrchestrationControlAction; etag: string }) =>
      controlOrchestration(vars.sessionId, vars.action, vars.etag),
    onSuccess: applySession,
  });
  const refresh = useMutation({
    mutationFn: (sessionId: string) => getOrchestrationPlan(sessionId),
    onSuccess: applySession,
  });
  const updatePlan = useMutation({
    mutationFn: (vars: { sessionId: string; planJson: string; etag: string }) =>
      updateOrchestrationPlan(vars.sessionId, vars.planJson, vars.etag),
    onSuccess: applySession,
  });
  const trace = useMutation({
    mutationFn: (sessionId: string) => getOrchestrationTrace(sessionId),
    onSuccess: setTraceItems,
  });

  const busy = submit.isPending || approve.isPending || control.isPending || refresh.isPending || updatePlan.isPending || trace.isPending;
  const activeError = submit.error ?? approve.error ?? control.error ?? refresh.error ?? updatePlan.error ?? trace.error;
  const canRun = can("orchestration:run");
  const canApprove = can("orchestration:approve");
  const canManage = can("orchestration:manage");
  const canEditPlan = Boolean(session && canRun && (session.status === "draft" || session.status === "pending_approval"));

  return (
    <Card className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-title-md text-on-surface">Điều phối tác nhân động</h2>
          <p className="text-label-sm text-on-surface-variant">
            Nhập mục tiêu, hệ thống lập kế hoạch nhiều tác nhân và thực thi theo DAG.
          </p>
        </div>
        {session && (
          <StatusPill tone={statusTone(session.status)}>{session.status}</StatusPill>
        )}
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
          <Button
            onClick={() => submit.mutate()}
            disabled={!canRun || busy || goal.trim().length === 0}
          >
            Gửi mục tiêu
          </Button>
          {session && (
            <Button variant="outline" onClick={() => refresh.mutate(session.sessionId)} disabled={busy}>
              Làm mới
            </Button>
          )}
        </div>
        {!canRun && (
          <p className="text-label-sm text-on-surface-variant">
            Bạn không có quyền chạy điều phối (orchestration:run).
          </p>
        )}
      </div>

      {activeError && <Alert tone="error">{errorMessage(activeError)}</Alert>}

      {session?.costBlocked && (
        <Alert tone="warning">
          Vượt ngưỡng chi phí ({session.costReason ?? "cost_cap"}). Kế hoạch chuyển sang chờ phê duyệt.
        </Alert>
      )}

      {session && (
        <div className="flex flex-col gap-3">
          <div className="flex flex-wrap items-center gap-2 text-label-sm text-on-surface-variant">
            <span>Mục tiêu: {session.goal}</span>
            {session.requiresApproval && <StatusPill tone="warning">Cần phê duyệt</StatusPill>}
            {session.replanCount > 0 && <span>Lập lại kế hoạch: {session.replanCount}</span>}
          </div>

          <div className="flex flex-col gap-2">
            <label className="text-label-sm text-on-surface-variant" htmlFor="orchestration-plan-json">
              Kế hoạch JSON có thể chỉnh sửa
            </label>
            <textarea
              id="orchestration-plan-json"
              value={planDraft}
              onChange={(e) => setPlanDraft(e.target.value)}
              rows={6}
              className="w-full rounded-lg border border-outline bg-surface-container-lowest p-3 font-mono text-mono-status text-on-surface focus:border-primary focus:outline-none"
              disabled={!canEditPlan || busy}
            />
            <div className="flex flex-wrap gap-2">
              <Button
                variant="outline"
                onClick={() => session && updatePlan.mutate({ sessionId: session.sessionId, planJson: planDraft, etag: session.etag })}
                disabled={!canEditPlan || busy || planDraft.trim().length === 0}
              >
                Lưu kế hoạch
              </Button>
              <Button
                variant="outline"
                onClick={() => session && trace.mutate(session.sessionId)}
                disabled={busy}
              >
                Tải trace
              </Button>
            </div>
          </div>

          <ul className="flex flex-col gap-2">
            {session.tasks.map((task) => (
              <li
                key={task.id}
                className="flex items-start justify-between gap-3 rounded-lg border border-outline p-3"
              >
                <div className="min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="font-mono text-mono-status text-on-surface-variant">{task.agent}</span>
                    <StatusPill tone={taskTone(task.status)}>{task.status}</StatusPill>
                  </div>
                  <p className="truncate text-body-md text-on-surface">{task.description}</p>
                  {task.dependsOn.length > 0 && (
                    <p className="text-label-sm text-on-surface-variant">phụ thuộc: {task.dependsOn.join(", ")}</p>
                  )}
                  {Object.keys(task.input).length > 0 && (
                    <p className="text-label-sm text-on-surface-variant">input: {JSON.stringify(task.input)}</p>
                  )}
                  {task.error && <p className="text-label-sm text-error">{task.error}</p>}
                </div>
              </li>
            ))}
          </ul>

          {traceItems.length > 0 && (
            <div className="rounded-lg border border-outline p-3">
              <h3 className="text-label-md text-on-surface">Trace điều phối</h3>
              <ul className="mt-2 flex flex-col gap-1">
                {traceItems.map((item, index) => (
                  <li key={`${item.taskId}-${item.phase}-${index}`} className="text-label-sm text-on-surface-variant">
                    <span className="font-mono">{item.phase}</span> · {item.agentName || item.taskId || "session"} · {item.message}
                  </li>
                ))}
              </ul>
            </div>
          )}

          <div className="flex flex-wrap items-center gap-2">
            {session.status === "pending_approval" && (
              <Button
                onClick={() => approve.mutate({ sessionId: session.sessionId, etag: session.etag })}
                disabled={!canApprove || busy}
              >
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
            {(session.status === "running" ||
              session.status === "paused") && (
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
