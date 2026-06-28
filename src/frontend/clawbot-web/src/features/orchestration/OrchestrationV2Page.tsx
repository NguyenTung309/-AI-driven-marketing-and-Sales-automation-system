import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AppShell } from "@/shared/layout/AppShell";
import { Alert } from "@/shared/ui/Alert";
import { Button } from "@/shared/ui/Button";
import { Card } from "@/shared/ui/Card";
import { Input } from "@/shared/ui/Input";
import { Modal } from "@/shared/ui/Modal";
import { StatusPill, type StatusTone } from "@/shared/ui/StatusPill";
import {
  createOrchestrationV2Run,
  createOrchestrationV2Schedule,
  getOrchestrationV2Run,
  listOrchestrationV2Agents,
  listOrchestrationV2Runs,
  listOrchestrationV2Schedules,
  runOrchestrationV2ScheduleNow,
  upsertOrchestrationV2Agent,
  type OrchestrationV2Agent,
  type OrchestrationV2RunDetail,
} from "@/shared/api/orchestrationV2";

const CADENCES = ["daily", "weekly", "monthly", "quarterly"] as const;

const CADENCE_LABELS: Record<(typeof CADENCES)[number], string> = {
  daily: "Hàng ngày",
  weekly: "Hàng tuần",
  monthly: "Hàng tháng",
  quarterly: "Hàng quý",
};

function cadenceLabel(cadence: string): string {
  const key = cadence.toLowerCase() as (typeof CADENCES)[number];
  return CADENCE_LABELS[key] ?? cadence;
}

function statusTone(status: string): StatusTone {
  const value = status.toLowerCase();
  if (value === "completed") return "success";
  if (value.includes("fail") || value === "cancelled") return "error";
  if (value.includes("skip")) return "neutral";
  return "warning";
}

function statusLabel(status: string): string {
  const value = status.toLowerCase();
  if (value === "completed") return "Hoàn thành";
  if (value.includes("fail")) return "Lỗi";
  if (value === "cancelled") return "Đã hủy";
  if (value.includes("skip")) return "Bỏ qua";
  if (value === "started") return "Đang chạy";
  if (value === "pending" || value === "planning") return "Đang xử lý";
  return status;
}

function formatDate(value: string | null): string {
  if (!value) return "—";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("vi-VN", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" }).format(date);
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : "Không thể tải dữ liệu điều phối.";
}

export default function OrchestrationV2Page() {
  const queryClient = useQueryClient();
  const [goal, setGoal] = useState("Ra mắt chiến dịch HSK4 vào quý tới");
  const [scheduleName, setScheduleName] = useState("Sàng lọc khách hàng tiềm năng hàng ngày");
  const [scheduleGoal, setScheduleGoal] = useState("Xem lại các khách hàng tiềm năng nóng và lên kế hoạch tiếp cận");
  const [cadence, setCadence] = useState<(typeof CADENCES)[number]>("daily");
  const [timezoneId, setTimezoneId] = useState("Asia/Ho_Chi_Minh");
  const [selectedRun, setSelectedRun] = useState<OrchestrationV2RunDetail | null>(null);
  const [queuedRunNow, setQueuedRunNow] = useState<string | null>(null);
  // SPEC-16 P1-7: agent tool allow-list editor state.
  const [editAgent, setEditAgent] = useState<OrchestrationV2Agent | null>(null);
  const [editAllowedTools, setEditAllowedTools] = useState("");

  const agents = useQuery({ queryKey: ["orchestration-v2", "agents"], queryFn: listOrchestrationV2Agents });
  const schedules = useQuery({ queryKey: ["orchestration-v2", "schedules"], queryFn: listOrchestrationV2Schedules });
  const runs = useQuery({ queryKey: ["orchestration-v2", "runs"], queryFn: listOrchestrationV2Runs });

  const refreshAll = async (): Promise<void> => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["orchestration-v2", "schedules"] }),
      queryClient.invalidateQueries({ queryKey: ["orchestration-v2", "runs"] }),
    ]);
  };

  const createRun = useMutation({
    mutationFn: () => createOrchestrationV2Run(goal.trim()),
    onSuccess: async (result) => {
      await refreshAll();
      setSelectedRun(await getOrchestrationV2Run(result.sessionId));
    },
  });
  const createSchedule = useMutation({
    mutationFn: () => createOrchestrationV2Schedule({ name: scheduleName.trim(), goalTemplate: scheduleGoal.trim(), cadence, timezoneId: timezoneId.trim(), requiresApproval: false }),
    onSuccess: refreshAll,
  });
  const runNow = useMutation({
    mutationFn: runOrchestrationV2ScheduleNow,
    onSuccess: async (result) => {
      setQueuedRunNow(`Đã xếp lịch chạy ngay (${statusLabel(result.status)}). Lần chạy tiếp theo: ${formatDate(result.nextRunAt)}. Hệ thống sẽ xử lý ở nhịp quét kế tiếp.`);
      await refreshAll();
    },
  });
  const loadRun = useMutation({ mutationFn: getOrchestrationV2Run, onSuccess: setSelectedRun });

  const saveAgent = useMutation({
    mutationFn: () => {
      if (!editAgent) throw new Error("No agent selected");
      return upsertOrchestrationV2Agent({
        code: editAgent.code,
        displayName: editAgent.displayName,
        agentType: editAgent.agentType,
        personaPrompt: editAgent.personaPrompt ?? "",
        isOrchestratable: editAgent.isOrchestratable,
        kbModuleCode: editAgent.kbModuleCode ?? null,
        allowedToolsJson: editAllowedTools.trim() || "[]",
      });
    },
    onSuccess: async () => {
      setEditAgent(null);
      await queryClient.invalidateQueries({ queryKey: ["orchestration-v2", "agents"] });
    },
  });

  const busy = createRun.isPending || createSchedule.isPending || runNow.isPending || loadRun.isPending;
  const activeError = createRun.error ?? createSchedule.error ?? runNow.error ?? loadRun.error ?? agents.error ?? schedules.error ?? runs.error;

  return (
    <AppShell title="Điều phối tự động">
      <div className="mb-4 text-label-sm text-on-surface-variant">Tạo lịch chạy định kỳ, xem các agent phụ và nhật ký phối hợp giữa các agent.</div>
      <div className="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
        <div className="flex flex-col gap-4">
          <Card className="flex flex-col gap-3">
            <div>
              <h2 className="text-title-md text-on-surface">Chạy thử theo mục tiêu</h2>
              <p className="text-label-sm text-on-surface-variant">Nhập một mục tiêu để hệ thống lập kế hoạch và điều phối các agent. Nhật ký chạy được ghi lại để xem lại.</p>
            </div>
            <textarea
              value={goal}
              onChange={(event) => setGoal(event.target.value)}
              rows={3}
              className="w-full rounded-lg border border-outline bg-surface-container-lowest p-3 text-body-md text-on-surface focus:border-primary focus:outline-none"
            />
            <Button disabled={busy || goal.trim().length === 0} onClick={() => createRun.mutate()}>
              Chạy ngay
            </Button>
          </Card>

          <Card className="flex flex-col gap-3">
            <div>
              <h2 className="text-title-md text-on-surface">Lịch chạy tự động</h2>
              <p className="text-label-sm text-on-surface-variant">Lên lịch chạy theo ngày, tuần, tháng hoặc quý theo múi giờ của bạn. Có thể bấm chạy thử ngay không cần chờ đến giờ.</p>
            </div>
            <div className="grid gap-3 md:grid-cols-2">
              <Input value={scheduleName} onChange={(event) => setScheduleName(event.target.value)} placeholder="Tên lịch" />
              <Input value={timezoneId} onChange={(event) => setTimezoneId(event.target.value)} placeholder="Asia/Ho_Chi_Minh" />
              <select className="rounded border border-surface-variant bg-surface-container-lowest px-3 py-2" value={cadence} onChange={(event) => setCadence(event.target.value as (typeof CADENCES)[number])}>
                {CADENCES.map((item) => <option key={item} value={item}>{CADENCE_LABELS[item]}</option>)}
              </select>
              <Input value={scheduleGoal} onChange={(event) => setScheduleGoal(event.target.value)} placeholder="Mục tiêu mẫu" />
            </div>
            <Button disabled={busy || scheduleName.trim().length === 0 || scheduleGoal.trim().length === 0} onClick={() => createSchedule.mutate()}>
              Tạo lịch
            </Button>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-label-sm">
                <thead className="text-on-surface-variant">
                  <tr><th>Tên lịch</th><th>Tần suất</th><th>Lần chạy kế</th><th>Trạng thái</th><th /></tr>
                </thead>
                <tbody>
                  {(schedules.data ?? []).map((schedule) => (
                    <tr key={schedule.id} className="border-t border-outline">
                      <td className="py-2 text-on-surface">{schedule.name}</td>
                      <td>{cadenceLabel(schedule.cadence)}</td>
                      <td>{formatDate(schedule.nextRunAt)}</td>
                      <td><StatusPill tone={schedule.isActive ? "success" : "neutral"}>{schedule.isActive ? "Đang bật" : "Tạm dừng"}</StatusPill></td>
                      <td><Button size="sm" variant="outline" disabled={busy} onClick={() => runNow.mutate(schedule.id)}>Chạy ngay</Button></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Card>
        </div>

        <div className="flex flex-col gap-4">
          {activeError && <Alert tone="error">{errorMessage(activeError)}</Alert>}
          {queuedRunNow && <Alert tone="success">{queuedRunNow}</Alert>}
          <Card className="flex flex-col gap-3">
            <h2 className="text-title-md text-on-surface">Các agent phụ</h2>
            <div className="grid gap-2">
              {(agents.data ?? []).map((agent) => (
                <div key={agent.id} className="rounded-lg border border-outline p-3">
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-mono text-mono-status">{agent.code}</span>
                    <div className="flex items-center gap-2">
                      <Button
                        variant="outline"
                        onClick={() => {
                          setEditAgent(agent);
                          setEditAllowedTools(agent.allowedToolsJson ?? "[]");
                        }}
                      >
                        Sửa tools
                      </Button>
                      <StatusPill tone={agent.isOrchestratable ? "success" : "neutral"}>Phiên bản {agent.version}</StatusPill>
                    </div>
                  </div>
                  <p className="text-body-md text-on-surface">{agent.displayName}</p>
                  <p className="text-label-sm text-on-surface-variant">{agent.agentType}</p>
                  {agent.allowedToolsJson && agent.allowedToolsJson !== "[]" ? (
                    <p className="mt-1 font-mono text-label-sm text-primary">tools: {agent.allowedToolsJson}</p>
                  ) : (
                    <p className="mt-1 text-label-sm text-on-surface-variant">chỉ văn bản (không tool)</p>
                  )}
                </div>
              ))}
            </div>
          </Card>

          <Card className="flex flex-col gap-3">
            <h2 className="text-title-md text-on-surface">Lần chạy gần đây</h2>
            <div className="flex flex-col gap-2">
              {(runs.data ?? []).map((run) => (
                <button key={run.sessionId} className="rounded-lg border border-outline p-3 text-left hover:bg-surface-container" onClick={() => loadRun.mutate(run.sessionId)} type="button">
                  <div className="flex items-center justify-between gap-2">
                    <span className="truncate text-body-md text-on-surface">{run.goal ?? run.sessionId}</span>
                    <StatusPill tone={statusTone(run.status)}>{statusLabel(run.status)}</StatusPill>
                  </div>
                  <p className="text-label-sm text-on-surface-variant">{formatDate(run.startedAt)}</p>
                </button>
              ))}
            </div>
          </Card>

          {selectedRun && (
            <Card className="flex flex-col gap-3">
              <div className="flex items-center justify-between gap-2">
                <h2 className="text-title-md text-on-surface">Nhật ký các bước</h2>
                <StatusPill tone={statusTone(selectedRun.status)}>{statusLabel(selectedRun.status)}</StatusPill>
              </div>
              <div className="flex flex-col gap-2">
                {selectedRun.traces.map((trace, index) => (
                  <div key={`${trace.phase}-${index}`} className="rounded border border-outline p-2 text-label-sm">
                    <span className="font-mono text-on-surface">{trace.phase}</span> · {trace.agentName || trace.taskId || "phiên chạy"}
                    <p className="text-on-surface-variant">{trace.message}</p>
                  </div>
                ))}
                {selectedRun.messages.map((message) => (
                  <div key={message.id} className="rounded border border-outline p-2 text-label-sm">
                    <span className="font-mono text-on-surface">{message.intent}:{message.status}</span> · {message.taskId}
                    {message.error && <p className="text-error">{message.error}</p>}
                  </div>
                ))}
              </div>
            </Card>
          )}
        </div>
      </div>

      {/* SPEC-16 P1-7: edit an agent's tool allow-list (drives the ReAct worker). */}
      <Modal
        open={editAgent !== null}
        onClose={() => setEditAgent(null)}
        title={`Cấu hình tools: ${editAgent?.code ?? ""}`}
        footer={
          <>
            <Button variant="ghost" onClick={() => setEditAgent(null)} disabled={saveAgent.isPending}>
              Hủy
            </Button>
            <Button onClick={() => saveAgent.mutate()} disabled={saveAgent.isPending}>
              Lưu
            </Button>
          </>
        }
      >
        {saveAgent.error ? <Alert tone="error">{errorMessage(saveAgent.error)}</Alert> : null}
        <p className="mb-2 text-body-sm text-on-surface-variant">
          Danh sách tool agent được phép gọi trong vòng ReAct (JSON array, ví dụ <code>["content-agent","content.approve"]</code>).
          Admin phải có quyền tương ứng của từng tool.
        </p>
        <textarea
          value={editAllowedTools}
          onChange={(e) => setEditAllowedTools(e.target.value)}
          rows={4}
          className="w-full rounded-lg border border-outline bg-surface-container-lowest p-3 font-mono text-mono-status text-on-surface focus:border-primary focus:outline-none"
        />
      </Modal>
    </AppShell>
  );
}
