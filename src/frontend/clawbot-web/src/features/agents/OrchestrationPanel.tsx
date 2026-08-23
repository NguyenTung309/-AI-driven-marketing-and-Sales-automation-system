import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { Alert } from "@/shared/ui/Alert";
import { Button } from "@/shared/ui/Button";
import { Card } from "@/shared/ui/Card";
import { Modal } from "@/shared/ui/Modal";
import { StatusPill } from "@/shared/ui/StatusPill";
import { StructuredData } from "@/shared/ui/StructuredData";
import { useAuthStore } from "@/shared/auth/authStore";
import { operationalPhaseLabel, toSafeOperationalText, toUserFriendlyOrchestrationError } from "@/shared/utils/userText";
import { TaskResultDetails } from "./TaskResultDetails";
import { TaskDagCanvas } from "./TaskDagCanvas";
import { TaskInterventionDialog, type TaskInterventionPayload } from "./TaskInterventionDialog";
import { a2aStatusLabel, statusLabel, statusTone, taskStatusLabel, taskTone } from "./orchestrationStatus";
import {
  approveOrchestrationV2Run,
  archiveOrchestrationV2Run,
  controlOrchestrationV2Run,
  createOrchestrationV2Run,
  getOrchestrationV2CostSummary,
  getOrchestrationV2Run,
  interveneOrchestrationV2Task,
  listOrchestrationV2Agents,
  listOrchestrationV2Runs,
  updateOrchestrationV2Plan,
  type OrchestrationV2ControlAction,
  type OrchestrationV2RunSummary,
  type OrchestrationV2Status,
  type OrchestrationV2Trace,
} from "@/shared/api/orchestrationV2";

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

// Khớp AutonomousOrchestratorOptions.PerTaskEstimateUsd mặc định (server có thể cấu hình khác).
const PER_TASK_ESTIMATE_USD = 0.01;

function failureExplanation(traces: readonly OrchestrationV2Trace[]): string | null {
  const haystack = traces.map((trace) => `${trace.phase} ${trace.message}`).join(" ");
  return toUserFriendlyOrchestrationError(haystack);
}

// Khớp AutonomousOrchestratorOptions.MaxRounds mặc định (server có thể cấu hình khác qua appsettings).
// Mặc định là 1 kể từ khi chính sách lỗi chuyển sang "pause": lập lại kế hoạch không còn là đường mặc định.
const DEFAULT_MAX_ROUNDS = 1;

// Mục tiêu mẫu — map đúng năng lực tool thật của orchestrator (content/research/lead/report).
// Chỉ nêu kênh mà publisher đăng được (facebook | instagram | zalo): gợi ý TikTok làm agent soạn bài
// rồi tắc ở bước đăng vì GraphSocialPublisher trả unsupported_platform.
const GOAL_TEMPLATES: readonly { readonly label: string; readonly goal: string }[] = [
  { label: "Lịch content tuần", goal: "Lên lịch content tuần này: quét xu hướng thị trường VN, chọn 3 chủ đề phù hợp, soạn bài Facebook và Instagram cho từng chủ đề rồi lên lịch đăng." },
  { label: "Chiến dịch ra mắt", goal: "Lập chiến dịch ra mắt khóa HSK4: nghiên cứu xu hướng, soạn nội dung đa kênh (Facebook, Instagram, Zalo) và trình duyệt trước khi đăng." },
  { label: "Báo cáo tháng", goal: "Tổng hợp báo cáo hiệu suất tháng này: số liệu KPI, bất thường cần chú ý và đề xuất hành động cho tháng sau." },
  { label: "Chăm sóc lead", goal: "Rà soát các lead mới trong 7 ngày qua, chấm điểm và đề xuất bước chăm sóc tiếp theo cho từng nhóm." },
  { label: "Nghiên cứu xu hướng", goal: "Quét xu hướng thị trường VN tuần này với từ khóa tiếng Trung, HSK; tóm tắt 5 chủ đề nổi bật kèm gợi ý nội dung." },
];

function errorMessage(error: unknown): string {
  if (error instanceof Error) return error.message;
  return "Đã xảy ra lỗi không xác định.";
}

interface OrchestrationPanelProps {
  readonly live?: boolean;
  readonly sessionId: string | null;
  readonly onSessionIdChange: (sessionId: string | null) => void;
}

export function OrchestrationPanel({ live = false, sessionId, onSessionIdChange }: OrchestrationPanelProps) {
  const permissions = useAuthStore((s) => s.permissions);
  const can = (code: string) => permissions.includes(code);
  const queryClient = useQueryClient();

  const [goal, setGoal] = useState("");
  // B9: dry-run — orchestrator vẫn lập kế hoạch + đi hết DAG nhưng tool chỉ trả preview "[dry-run] would ...".
  const [dryRun, setDryRun] = useState(false);
  // Editable plan draft. `planSource` tracks which fetched plan the draft was seeded from, so we can
  // re-seed during render when a new plan arrives (React's "adjust state on prop change" pattern)
  // without an effect-driven cascading render.
  const [draft, setDraft] = useState<{ source: string; text: string } | null>(null);
  // SPEC-16 P3-1: raw-JSON plan edit is behind an "advanced" toggle so the structured DAG is the primary view.
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [showRecentRuns, setShowRecentRuns] = useState(false);
  // Node the user clicked on the DAG; falls back to the running task so the detail pane follows execution.
  const [selectedTaskId, setSelectedTaskId] = useState<string | null>(null);
  // Dialog can thiệp một bước (sửa output / chạy lại / bỏ qua) khi phiên đang tạm dừng.
  const [showIntervene, setShowIntervene] = useState(false);
  const [showAgentPrompt, setShowAgentPrompt] = useState(false);

  const setSessionId = (next: string | null): void => onSessionIdChange(next);

  // Durable session state: read by sessionId from the URL so it survives route changes and F5.
  const sessionQuery = useQuery({
    queryKey: ["orchestration", "session", sessionId],
    queryFn: () => getOrchestrationV2Run(sessionId!),
    enabled: Boolean(sessionId),
    // C1: realtime đẩy invalidation qua SignalR — polling chỉ còn là dự phòng 30s khi hub sống.
    refetchInterval: (query) =>
      query.state.data && ACTIVE_STATUSES.has(query.state.data.status) ? (live ? 30_000 : POLL_INTERVAL_MS) : false,
  });
  const session = sessionQuery.data ?? null;

  // SPEC-16 P3-6: recent/in-flight run list (URL-independent) so the user can switch runs without a sessionId in hand.
  const runsQuery = useQuery({
    queryKey: ["orchestration", "runs"],
    queryFn: () => listOrchestrationV2Runs(false),
    refetchInterval: live ? 30_000 : POLL_INTERVAL_MS,
  });

  // Traces are embedded in the run-detail response, so no separate polling query is needed.
  const traceItems = useMemo(() => session?.traces ?? [], [session?.traces]);

  // B5: guardrail chi phí hiển thị tại điểm phê duyệt — cùng ledger với cost guard của orchestrator.
  const costSummaryQuery = useQuery({
    queryKey: ["orchestration", "cost-summary"],
    queryFn: getOrchestrationV2CostSummary,
    enabled: session?.status === "pending_approval",
    staleTime: 60_000,
  });

  // Group tool-action traces (tool_executed/tool_failed/tool_blocked/…) by the task that produced them, so each
  // agent step can show exactly what it did on the system.
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

  // Bước có kết quả do người dùng sửa tay — đánh dấu để không nhầm là output của agent.
  const editedTaskIds = useMemo(
    () => new Set(traceItems.filter((trace) => trace.phase === "task_edited").map((trace) => trace.taskId)),
    [traceItems],
  );

  const sessionTasks = session?.tasks ?? [];
  const runningTask = sessionTasks.find((task) => task.status === "running") ?? null;
  const selectedTask = sessionTasks.find((task) => task.id === selectedTaskId) ?? runningTask ?? sessionTasks[0] ?? null;
  const taskMessages = selectedTask
    ? (session?.messages ?? []).filter((message) => message.taskId === selectedTask.id)
    : [];
  const canViewAgentPrompts = can("orchestration:view");
  const agentDefinitionsQuery = useQuery({
    queryKey: ["orchestration-v2", "agents"],
    queryFn: listOrchestrationV2Agents,
    enabled: showAgentPrompt && canViewAgentPrompts,
    staleTime: 5 * 60_000,
  });
  const selectedAgentDefinition = useMemo(() => {
    if (!selectedTask) return null;
    const code = selectedTask.agent.toLocaleLowerCase();
    return agentDefinitionsQuery.data?.find((agent) => agent.code.toLocaleLowerCase() === code) ?? null;
  }, [agentDefinitionsQuery.data, selectedTask]);
  const awaitingApprovalTaskId = useMemo(() => {
    for (let index = traceItems.length - 1; index >= 0; index -= 1) {
      const trace = traceItems[index];
      if (trace.phase === "awaiting_approval") return trace.taskId;
    }
    return null;
  }, [traceItems]);
  const isSelectedTaskAwaitingApproval = Boolean(
    session?.status === "paused"
      && selectedTask?.status === "completed"
      && selectedTask.id === awaitingApprovalTaskId,
  );

  // Re-seed the editable draft when the fetched plan changes (keyed by sessionId + planJson).
  const planSource = session ? `${session.sessionId}:${session.planJson}` : null;
  if (planSource && draft?.source !== planSource) {
    setDraft({ source: planSource, text: session!.planJson });
  }
  const planDraft = draft?.text ?? "";
  const setPlanDraft = (text: string): void =>
    setDraft((current) => ({ source: current?.source ?? planSource ?? "", text }));

  const submit = useMutation({
    mutationFn: () => createOrchestrationV2Run(goal.trim(), dryRun),
    onSuccess: (result) => setSessionId(result.sessionId),
  });
  // Chạy lại mục tiêu của bản chạy thử ở chế độ thật.
  const rerunReal = useMutation({
    mutationFn: (runGoal: string) => createOrchestrationV2Run(runGoal, false),
    onSuccess: (result) => {
      setDryRun(false);
      setSessionId(result.sessionId);
    },
  });
  const approve = useMutation({
    mutationFn: (vars: { sessionId: string; etag: string }) => approveOrchestrationV2Run(vars.sessionId, vars.etag),
    onSuccess: () => sessionQuery.refetch(),
  });
  const control = useMutation({
    mutationFn: (vars: { sessionId: string; action: OrchestrationV2ControlAction; etag: string }) =>
      controlOrchestrationV2Run(vars.sessionId, vars.action, vars.etag),
    onSuccess: () => sessionQuery.refetch(),
  });
  const updatePlan = useMutation({
    mutationFn: (vars: { sessionId: string; planJson: string; etag: string }) =>
      updateOrchestrationV2Plan(vars.sessionId, vars.planJson, vars.etag),
    onSuccess: () => sessionQuery.refetch(),
  });
  // Can thiệp một bước: server sửa plan (redact + validate) rồi tùy chọn chạy tiếp ngay. Etag để chạy tiếp
  // lấy từ chính response của intervene — plan vừa đổi nên etag cũ trong cache đã cũ.
  const intervene = useMutation({
    mutationFn: async (vars: { sessionId: string; taskId: string; etag: string; payload: TaskInterventionPayload }) => {
      const plan = await interveneOrchestrationV2Task(vars.sessionId, vars.taskId, {
        action: vars.payload.action,
        output: vars.payload.output,
        rerunDownstream: vars.payload.rerunDownstream,
        etag: vars.etag,
      });
      if (vars.payload.resumeAfter) await controlOrchestrationV2Run(vars.sessionId, "resume", plan.etag);
      return plan;
    },
    onSuccess: async () => {
      setShowIntervene(false);
      await sessionQuery.refetch();
    },
  });
  const archiveRun = useMutation({
    mutationFn: archiveOrchestrationV2Run,
    onSuccess: async (_data, archivedSessionId) => {
      if (sessionId === archivedSessionId) setSessionId(null);
      await queryClient.invalidateQueries({ queryKey: ["orchestration", "runs"] });
    },
  });
  // Run summary không mang etag mà control lại bắt buộc etag → fetch detail lấy etag mới trước khi hủy
  // (fix lỗi 409 cố hữu của nút "Hủy" trong danh sách phiên).
  const cancelRun = useMutation({
    mutationFn: async (runSessionId: string) => {
      const detail = await getOrchestrationV2Run(runSessionId);
      return controlOrchestrationV2Run(runSessionId, "cancel", detail.etag);
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["orchestration", "runs"] });
    },
  });

  const busy = submit.isPending || approve.isPending || control.isPending || updatePlan.isPending || archiveRun.isPending || cancelRun.isPending || rerunReal.isPending || intervene.isPending;
  const activeError = submit.error ?? approve.error ?? control.error ?? updatePlan.error ?? archiveRun.error ?? cancelRun.error ?? rerunReal.error ?? sessionQuery.error;
  const canRun = can("orchestration:run");
  const canApprove = can("orchestration:approve");
  const canManage = can("orchestration:manage");
  const canEditPlan = Boolean(session && canRun && (session.status === "draft" || session.status === "pending_approval" || session.status === "paused"));
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
        <div className="flex items-center gap-2">
          <span className="flex items-center gap-1 text-label-sm text-on-surface-variant" title={live ? "Cập nhật đẩy qua SignalR" : "Hub cập nhật tức thì chưa kết nối — tự làm mới định kỳ"}>
            <span aria-hidden="true" className={`size-2 rounded-full ${live ? "bg-success" : "bg-on-surface-variant/40"}`} />
            {live ? null : "Dự phòng"}
          </span>
          {session && traceItems.some((trace) => trace.phase === "dry_run") && (
            <StatusPill tone="warning">Bản chạy thử</StatusPill>
          )}
          {session && <StatusPill tone={statusTone(session.status)}>{statusLabel(session.status)}</StatusPill>}
        </div>
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
        {goal.trim().length === 0 && canRun && (
          <div className="flex flex-wrap gap-2">
            {GOAL_TEMPLATES.map((template) => (
              <button
                className="rounded-full border border-outline bg-surface-container-lowest px-3 py-1 text-label-sm text-secondary transition-colors hover:border-primary hover:text-primary"
                key={template.label}
                onClick={() => setGoal(template.goal)}
                type="button"
              >
                {template.label}
              </button>
            ))}
          </div>
        )}
        <label className="flex items-center gap-2 text-label-sm text-on-surface-variant">
          <input checked={dryRun} disabled={!canRun || busy} onChange={(event) => setDryRun(event.target.checked)} type="checkbox" />
          Chạy thử — công cụ chỉ mô phỏng hành động, không thực thi thật
        </label>
        <div className="flex items-center gap-2">
          <Button onClick={() => submit.mutate()} disabled={!canRun || busy || goal.trim().length === 0}>
            {dryRun ? "Chạy thử mục tiêu" : "Gửi mục tiêu"}
          </Button>
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
          <div className="flex items-center gap-2">
            <Button variant="outline" onClick={() => setShowRecentRuns(true)} disabled={busy}>
              Xem phiên
            </Button>
            <Link className="text-label-sm text-primary hover:underline" to="/agents/runs">
              Tất cả phiên ↗
            </Link>
          </div>
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

      {session?.status === "pending_approval" && (
        <Alert tone="warning">
          {canApprove
            ? "Kế hoạch đang chờ duyệt — xem DAG bên dưới rồi bấm \"Phê duyệt & chạy\"."
            : "Kế hoạch đang chờ người có quyền phê duyệt (orchestration:approve) xử lý."}
        </Alert>
      )}

      {session?.status === "failed" && (
        <Alert tone="error">
          <span className="flex flex-col gap-2">
            <span>
              {failureExplanation(traceItems)
                ?? "Lập kế hoạch hoặc thực thi thất bại. Xem nhật ký bên dưới để biết bước bị lỗi, chỉnh lại mục tiêu rồi gửi lại."}
            </span>
            <button
              className="self-start font-bold underline"
              onClick={() => setGoal(session.goal)}
              type="button"
            >
              Gửi lại mục tiêu này
            </button>
          </span>
        </Alert>
      )}

      {session?.costBlocked && (
        <Alert tone="warning">
          Vượt ngưỡng chi phí ({session.costReason ?? "cost_cap"}). Kế hoạch chuyển sang chờ phê duyệt.
        </Alert>
      )}

      {session?.status === "pause_requested" && (
        <Alert tone="info">
          Đang dừng an toàn — chờ bước hiện tại kết thúc rồi mới sửa được, để bản sửa không bị agent ghi đè.
        </Alert>
      )}

      {/* Thay cho việc tự lập lại kế hoạch: phiên dừng ngay tại bước lỗi, người dùng sửa rồi chạy tiếp. */}
      {session?.status === "paused" && session.tasks.some((task) => task.status === "failed") && (
        <Alert tone="warning">
          <span className="flex flex-col gap-2">
            <span>
              Phiên dừng ở bước lỗi để bạn xử lý — chưa tốn thêm chi phí lập kế hoạch lại. Chọn bước lỗi trên sơ đồ,
              sửa kết quả hoặc cho chạy lại, rồi bấm chạy tiếp.
            </span>
            <button
              className="self-start font-bold underline"
              onClick={() => {
                const failed = session.tasks.find((task) => task.status === "failed");
                if (!failed) return;
                setSelectedTaskId(failed.id);
                setShowIntervene(true);
              }}
              type="button"
            >
              Mở bước lỗi để xử lý
            </button>
          </span>
        </Alert>
      )}

      <Modal open={showRecentRuns} onClose={() => setShowRecentRuns(false)} title="Phiên gần đây">
        {runsQuery.isFetching && <p className="text-label-sm text-on-surface-variant">Đang cập nhật...</p>}
        {runsQuery.data && runsQuery.data.length > 0 ? (
          <ul className="flex max-h-[60vh] flex-col gap-2 overflow-y-auto pr-1">
            {runsQuery.data.map((run: OrchestrationV2RunSummary) => {
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
                      {new Date(run.startedAt).toLocaleString("vi-VN")}
                    </span>
                  </button>
                  {canCancelRun ? (
                    <Button
                      variant="ghost"
                      onClick={() => {
                        if (window.confirm("Hủy phiên này? Các task đang chạy sẽ dừng lại.")) cancelRun.mutate(run.sessionId);
                      }}
                      disabled={!canManage || busy}
                      title={!canManage ? "Cần quyền orchestration:manage" : undefined}
                    >
                      Hủy
                    </Button>
                  ) : null}
                  {canArchiveRun ? (
                    <Button
                      variant="ghost"
                      onClick={() => archiveRun.mutate(run.sessionId)}
                      disabled={!canManage || busy}
                      title={!canManage ? "Cần quyền orchestration:manage" : undefined}
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
            {session.replanCount > 0 && (
              <StatusPill tone={session.replanCount >= DEFAULT_MAX_ROUNDS ? "error" : "warning"}>
                Lập lại kế hoạch {session.replanCount}/{DEFAULT_MAX_ROUNDS}
              </StatusPill>
            )}
            {session.actualCostUsd > 0 && (
              <span title="Chi phí LLM thực của phiên này">💵 Chi phí thực: ${session.actualCostUsd.toFixed(4)}</span>
            )}
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
                Chỉ chỉnh sửa được khi phiên ở trạng thái Nháp, Chờ phê duyệt hoặc Tạm dừng.
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

          <TaskInterventionDialog
            approvalOnly={isSelectedTaskAwaitingApproval}
            busy={intervene.isPending}
            error={intervene.error ? errorMessage(intervene.error) : null}
            onClose={() => setShowIntervene(false)}
            onSubmit={(payload) => {
              if (!selectedTask) return;
              intervene.mutate({ sessionId: session.sessionId, taskId: selectedTask.id, etag: session.etag, payload });
            }}
            open={showIntervene}
            task={selectedTask}
            tasks={session.tasks}
          />

          <Modal
            open={showAgentPrompt}
            onClose={() => setShowAgentPrompt(false)}
            title={`System prompt & quy tắc: ${selectedTask?.agent ?? "agent"}`}
            maxWidthClass="max-w-3xl"
          >
            {agentDefinitionsQuery.isLoading ? (
              <p className="text-label-sm text-on-surface-variant">Đang tải cấu hình agent...</p>
            ) : selectedAgentDefinition ? (
              <div className="flex flex-col gap-4">
                <section aria-labelledby="agent-prompt-heading">
                  <h3 className="text-label-md text-on-surface" id="agent-prompt-heading">System prompt cấu hình</h3>
                  <pre className="mt-2 max-h-[42vh] overflow-auto whitespace-pre-wrap break-words rounded-lg border border-outline bg-surface-container-low p-3 font-mono text-mono-status text-on-surface">
                    {selectedAgentDefinition.systemPrompt || selectedAgentDefinition.personaPrompt || "Agent chưa có system prompt riêng."}
                  </pre>
                </section>
                <section aria-labelledby="agent-tools-heading">
                  <h3 className="text-label-md text-on-surface" id="agent-tools-heading">Công cụ được phép</h3>
                  <pre className="mt-2 max-h-40 overflow-auto whitespace-pre-wrap break-words rounded-lg border border-outline bg-surface-container-low p-3 font-mono text-mono-status text-on-surface">
                    {selectedAgentDefinition.allowedToolsJson || "[]"}
                  </pre>
                </section>
                {selectedTask ? (
                  <section aria-labelledby="task-instruction-heading">
                    <h3 className="text-label-md text-on-surface" id="task-instruction-heading">Hướng dẫn cho task đang duyệt</h3>
                    <p className="mt-2 text-body-md text-on-surface">{selectedTask.description}</p>
                  </section>
                ) : null}
                <p className="text-label-sm text-on-surface-variant">
                  Quy tắc an toàn nền tảng được hệ thống ghép cố định khi chạy agent và không chỉnh sửa ở màn hình này.
                </p>
              </div>
            ) : (
              <p className="text-label-sm text-on-surface-variant">
                Không tìm thấy cấu hình của agent này trong danh mục hiện tại. Agent có thể đã bị xóa hoặc đổi mã sau khi phiên bắt đầu.
              </p>
            )}
          </Modal>

          <TaskDagCanvas
            onSelect={setSelectedTaskId}
            selectedTaskId={selectedTask?.id ?? null}
            tasks={session.tasks}
          />

          {selectedTask && (
            <div className="rounded-lg border border-outline bg-surface-container-lowest p-3">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-mono text-mono-status text-secondary">{selectedTask.agent}</span>
                <StatusPill tone={taskTone(selectedTask.status)}>{taskStatusLabel(selectedTask.status)}</StatusPill>
                {selectedTask.dependsOn.length > 0 && (
                  <span className="text-label-sm text-on-surface-variant">
                    Nhận đầu vào từ: {selectedTask.dependsOn.join(", ")}
                  </span>
                )}
              </div>
              <p className="mt-2 text-body-md text-on-surface">{selectedTask.description}</p>
              {selectedTask.error && (
                <p className="mt-1 text-label-sm text-error">
                  {toUserFriendlyOrchestrationError(selectedTask.error) ?? selectedTask.error}
                </p>
              )}
              <TaskResultDetails
                editedByUser={editedTaskIds.has(selectedTask.id)}
                task={selectedTask}
                toolTraces={toolTracesByTask.get(selectedTask.id) ?? []}
              />
              {isSelectedTaskAwaitingApproval ? (
                <div className="mt-3 flex flex-wrap gap-2">
                  <Button
                    disabled={!canManage || busy}
                    onClick={() => control.mutate({ sessionId: session.sessionId, action: "resume", etag: session.etag })}
                    size="sm"
                    title={!canManage ? "Cần quyền orchestration:manage" : undefined}
                  >
                    Duyệt
                  </Button>
                  <Button
                    disabled={!canManage || busy}
                    onClick={() => setShowIntervene(true)}
                    size="sm"
                    title={!canManage ? "Cần quyền orchestration:manage" : undefined}
                    variant="outline"
                  >
                    Sửa nội dung
                  </Button>
                  <Button
                    disabled={!canViewAgentPrompts || busy}
                    onClick={() => setShowAgentPrompt(true)}
                    size="sm"
                    title={!canViewAgentPrompts ? "Cần quyền orchestration:view" : undefined}
                    variant="ghost"
                  >
                    Xem system prompt &amp; quy tắc
                  </Button>
                </div>
              ) : session.status === "paused" ? (
                <Button
                  className="mt-3"
                  disabled={!canManage || busy}
                  onClick={() => setShowIntervene(true)}
                  size="sm"
                  title={!canManage ? "Cần quyền orchestration:manage" : undefined}
                  variant="outline"
                >
                  Sửa kết quả bước này
                </Button>
              ) : null}
              {/* Đang chạy: muốn sửa thì phải dừng an toàn trước — sửa khi runner còn giữ plan trong bộ nhớ sẽ bị ghi đè. */}
              {session.status === "running" && selectedTask.status === "completed" ? (
                <Button
                  className="mt-3"
                  disabled={!canManage || busy}
                  onClick={() => control.mutate({ sessionId: session.sessionId, action: "pause", etag: session.etag })}
                  size="sm"
                  title={!canManage ? "Cần quyền orchestration:manage" : "Dừng sau khi bước hiện tại kết thúc, rồi mới sửa được"}
                  variant="ghost"
                >
                  Tạm dừng để sửa bước này
                </Button>
              ) : null}
              {taskMessages.length > 0 && (
                <details className="mt-3">
                  <summary className="cursor-pointer text-label-sm text-primary">
                    Tin nhắn A2A giữa các agent ({taskMessages.length})
                  </summary>
                  <ul className="mt-2 flex flex-col gap-2">
                    {taskMessages.map((message) => (
                      <li className="rounded border border-outline bg-surface p-2" key={message.id}>
                        <div className="flex flex-wrap items-center gap-2 text-label-sm">
                          <span className="font-mono text-secondary">{message.intent}</span>
                          <StatusPill tone={message.error ? "error" : message.processedAt ? "success" : "warning"}>
                            {a2aStatusLabel(message.status)}
                          </StatusPill>
                          <span className="text-on-surface-variant">{new Date(message.createdAt).toLocaleString("vi-VN")}</span>
                        </div>
                        {message.error ? <p className="mt-1 text-label-sm text-error">{message.error}</p> : null}
                        <div className="mt-1">
                          <StructuredData maxHeightClass="max-h-32" value={message.payloadJson} />
                        </div>
                      </li>
                    ))}
                  </ul>
                </details>
              )}
            </div>
          )}

          {sessionId && (
            <Link
              className="self-start text-label-sm text-primary hover:underline"
              to={`/agents/runs/${encodeURIComponent(sessionId)}`}
            >
              Mở chi tiết phiên ↗
            </Link>
          )}

          <div className="rounded-lg border border-outline p-3">
            <div className="flex items-center justify-between">
              <h3 className="text-label-md text-on-surface">Nhật ký điều phối</h3>
              {sessionQuery.isFetching && <span className="text-label-sm text-on-surface-variant">đang cập nhật...</span>}
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
            {session.status === "pending_approval" && session.tasks.length > 0 && (
              <span className="text-label-sm text-on-surface-variant">
                Ước tính ~${(session.tasks.length * PER_TASK_ESTIMATE_USD).toFixed(2)} ({session.tasks.length} task × ${PER_TASK_ESTIMATE_USD.toFixed(2)})
              </span>
            )}
            {session.status === "pending_approval" && costSummaryQuery.data && (
              <span
                className={`text-label-sm ${
                  costSummaryQuery.data.capUsd > 0 && costSummaryQuery.data.monthToDateUsd / costSummaryQuery.data.capUsd > 0.8
                    ? "font-bold text-error"
                    : "text-on-surface-variant"
                }`}
              >
                · Đã dùng ${costSummaryQuery.data.monthToDateUsd.toFixed(2)} / hạn mức ${costSummaryQuery.data.capUsd.toFixed(0)} tháng này
              </span>
            )}
            {traceItems.some((trace) => trace.phase === "dry_run")
              && (session.status === "completed" || session.status === "pending_approval") && (
              <Button disabled={!canRun || busy} onClick={() => rerunReal.mutate(session.goal)} variant="outline">
                Chạy thật với mục tiêu này
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
                onClick={() => {
                  if (window.confirm("Hủy phiên này? Các task đang chạy sẽ dừng lại.")) {
                    control.mutate({ sessionId: session.sessionId, action: "cancel", etag: session.etag });
                  }
                }}
                disabled={!canManage || busy}
                title={!canManage ? "Cần quyền orchestration:manage" : undefined}
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
