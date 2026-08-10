import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Button, Card, ConfirmDialog, Input, Modal, StatusPill } from "@/shared/ui";
import { useAuthStore } from "@/shared/auth/authStore";
import {
  activateOrchestrationV2Schedule,
  createOrchestrationV2Schedule,
  deleteOrchestrationV2Schedule,
  listOrchestrationV2Schedules,
  pauseOrchestrationV2Schedule,
  runOrchestrationV2ScheduleNow,
  type OrchestrationV2Schedule,
} from "@/shared/api/orchestrationV2";

const CADENCE_LABELS: Record<string, string> = {
  daily: "Hằng ngày",
  weekly: "Hằng tuần",
  monthly: "Hằng tháng",
  quarterly: "Hằng quý",
};

const CADENCE_OPTIONS = ["daily", "weekly", "monthly", "quarterly"] as const;

// C2: catalog event đã nối dây dispatcher phía backend (ScheduleEventKeys).
const EVENT_OPTIONS = [
  { value: "content.trends.scanned", label: "Khi quét xu hướng xong" },
  { value: "lead.became_hot", label: "Khi có lead nóng" },
  { value: "content.publish.failed", label: "Khi đăng bài thất bại (hết lượt thử lại)" },
] as const;

function eventLabel(key: string | null | undefined): string {
  return EVENT_OPTIONS.find((option) => option.value === key)?.label ?? key ?? "";
}

// Lịch event ngủ ở NextRunAt = 9999 — hiển thị "Chờ sự kiện" thay vì ngày ảo.
function isWaitingForEvent(nextRunAt: string): boolean {
  return new Date(nextRunAt).getFullYear() > 9000;
}

const SELECT_CLASS =
  "bg-surface-container-lowest border border-surface-variant rounded px-3 py-2 text-body-md w-full focus:outline-none focus:ring-2 focus:ring-primary/30";

function errorMessage(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as Error & { response?: { data?: { message?: unknown; error?: unknown; detail?: unknown } } }).response;
    if (typeof response?.data?.message === "string") return response.data.message;
    if (typeof response?.data?.detail === "string") return response.data.detail;
    if (typeof response?.data?.error === "string") return response.data.error;
    return error.message;
  }
  return "Đã xảy ra lỗi không xác định.";
}

// B2: schedules sống ngay trên /agents (trang /orchestration cũ đã gỡ) — tạo lịch giao mục tiêu
// định kỳ cho orchestrator, bật/tắt, chạy ngay.
export function SchedulesCard() {
  const queryClient = useQueryClient();
  const permissions = useAuthStore((s) => s.permissions);
  const canManage = permissions.includes("orchestration:manage");
  const canRun = permissions.includes("orchestration:run");

  const schedulesQuery = useQuery({ queryKey: ["orchestration", "schedules"], queryFn: listOrchestrationV2Schedules });
  const invalidateSchedules = () => queryClient.invalidateQueries({ queryKey: ["orchestration", "schedules"] });
  const invalidateSchedulesAndRuns = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ["orchestration", "schedules"] }),
      queryClient.invalidateQueries({ queryKey: ["orchestration", "runs"] }),
    ]);

  const [createOpen, setCreateOpen] = useState(false);
  const [name, setName] = useState("");
  const [goalTemplate, setGoalTemplate] = useState("");
  const [cadence, setCadence] = useState<string>("weekly");
  const [triggerType, setTriggerType] = useState<"cadence" | "event">("cadence");
  const [eventKey, setEventKey] = useState<string>(EVENT_OPTIONS[0].value);
  const [scheduleToDelete, setScheduleToDelete] = useState<OrchestrationV2Schedule | null>(null);

  const createMutation = useMutation({
    mutationFn: () =>
      createOrchestrationV2Schedule({
        name: name.trim(),
        goalTemplate: goalTemplate.trim(),
        cadence,
        timezoneId: "Asia/Ho_Chi_Minh",
        triggerType,
        eventKey: triggerType === "event" ? eventKey : null,
      }),
    onSuccess: async () => {
      await invalidateSchedules();
      setCreateOpen(false);
      setName("");
      setGoalTemplate("");
    },
  });
  const pauseMutation = useMutation({ mutationFn: pauseOrchestrationV2Schedule, onSuccess: invalidateSchedules });
  const activateMutation = useMutation({ mutationFn: activateOrchestrationV2Schedule, onSuccess: invalidateSchedules });
  const runNowMutation = useMutation({ mutationFn: runOrchestrationV2ScheduleNow, onSuccess: invalidateSchedulesAndRuns });
  const deleteMutation = useMutation({
    mutationFn: deleteOrchestrationV2Schedule,
    onSuccess: async () => {
      await invalidateSchedules();
      setScheduleToDelete(null);
    },
  });

  const busy = createMutation.isPending || pauseMutation.isPending || activateMutation.isPending || runNowMutation.isPending || deleteMutation.isPending;
  const error = schedulesQuery.error ?? createMutation.error ?? pauseMutation.error ?? activateMutation.error ?? runNowMutation.error ?? deleteMutation.error;
  const schedules = schedulesQuery.data ?? [];

  return (
    <Card className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="text-title-md text-on-surface">Lịch tự động</h2>
          <p className="text-label-sm text-on-surface-variant">
            Giao mục tiêu cho orchestrator theo chu kỳ — chạy trong vòng 1 phút sau mốc hẹn.
          </p>
        </div>
        <Button
          disabled={!canManage}
          onClick={() => setCreateOpen(true)}
          title={!canManage ? "Cần quyền orchestration:manage" : undefined}
          variant="outline"
        >
          <span aria-hidden="true" className="material-symbols-outlined text-[18px]">add</span>
          Tạo lịch
        </Button>
      </div>

      {error ? <Alert tone="error">{errorMessage(error)}</Alert> : null}

      {schedulesQuery.isError ? null : schedules.length ? (
        <ul className="flex flex-col gap-2">
          {schedules.map((schedule: OrchestrationV2Schedule) => (
            <li className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-outline p-3" key={schedule.id}>
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-semibold text-on-surface">{schedule.name}</span>
                  <StatusPill tone={schedule.isActive ? "success" : "neutral"}>
                    {schedule.isActive ? "Đang bật" : "Tạm dừng"}
                  </StatusPill>
                  <span className="text-label-sm text-on-surface-variant">
                    {schedule.triggerType === "event"
                      ? `Sự kiện: ${eventLabel(schedule.eventKey)}`
                      : CADENCE_LABELS[schedule.cadence] ?? schedule.cadence}
                  </span>
                  {schedule.requiresApproval ? <StatusPill tone="warning">Cần duyệt</StatusPill> : null}
                </div>
                <p className="max-w-[520px] truncate text-label-sm text-on-surface-variant" title={schedule.goalTemplate}>
                  {schedule.goalTemplate}
                </p>
                <div className="flex flex-wrap items-center gap-1 text-label-sm text-on-surface-variant">
                  <span>Kế tiếp: {isWaitingForEvent(schedule.nextRunAt) ? "Chờ sự kiện" : new Date(schedule.nextRunAt).toLocaleString("vi-VN")}</span>
                  {schedule.lastRunAt ? <span>· Lần cuối: {new Date(schedule.lastRunAt).toLocaleString("vi-VN")}</span> : null}
                  {schedule.lastRunStatus === "failed" || schedule.lastRunStatus === "cancelled" ? (
                    <span title={schedule.lastRunError ?? "Lần chạy gần nhất không hoàn tất."}>
                      <StatusPill tone="error">Lần cuối: lỗi</StatusPill>
                    </span>
                  ) : null}
                </div>
              </div>
              <div className="flex items-center gap-2">
                <Button
                  disabled={!canRun || busy}
                  onClick={() => runNowMutation.mutate(schedule.id)}
                  size="sm"
                  title={!canRun ? "Cần quyền orchestration:run" : undefined}
                  variant="outline"
                >
                  Chạy ngay
                </Button>
                {schedule.isActive ? (
                  <Button
                    disabled={!canManage || busy}
                    onClick={() => pauseMutation.mutate(schedule.id)}
                    size="sm"
                    title={!canManage ? "Cần quyền orchestration:manage" : undefined}
                    variant="ghost"
                  >
                    Tạm dừng
                  </Button>
                ) : (
                  <Button
                    disabled={!canManage || busy}
                    onClick={() => activateMutation.mutate(schedule.id)}
                    size="sm"
                    title={!canManage ? "Cần quyền orchestration:manage" : undefined}
                    variant="ghost"
                  >
                    Bật lại
                  </Button>
                )}
                <Button
                  className="border-error text-error hover:bg-error/10"
                  disabled={!canManage || busy}
                  onClick={() => setScheduleToDelete(schedule)}
                  size="sm"
                  title={!canManage ? "Cần quyền orchestration:manage" : undefined}
                  variant="outline"
                >
                  Xóa
                </Button>
              </div>
            </li>
          ))}
        </ul>
      ) : schedulesQuery.isLoading ? (
        <p className="text-label-sm text-on-surface-variant">Đang tải lịch...</p>
      ) : (
        <p className="text-label-sm text-on-surface-variant">
          Chưa có lịch nào. Tạo lịch để orchestrator tự chạy mục tiêu định kỳ (vd. lịch content tuần).
        </p>
      )}

      <Modal
        footer={
          <>
            <Button disabled={busy} onClick={() => setCreateOpen(false)} variant="outline">
              Hủy
            </Button>
            <Button
              disabled={busy || name.trim().length < 2 || goalTemplate.trim().length < 10}
              onClick={() => createMutation.mutate()}
            >
              {createMutation.isPending ? "Đang tạo..." : "Tạo lịch"}
            </Button>
          </>
        }
        maxWidthClass="max-w-xl"
        onClose={() => setCreateOpen(false)}
        open={createOpen}
        title="Tạo lịch tự động"
      >
        <div className="space-y-4">
          <div>
            <label className="mb-1 block text-body-md font-bold text-secondary" htmlFor="schedule-name">
              Tên lịch
            </label>
            <Input id="schedule-name" onChange={(event) => setName(event.target.value)} placeholder="vd: Content tuần" value={name} />
          </div>
          <div>
            <label className="mb-1 block text-body-md font-bold text-secondary" htmlFor="schedule-goal">
              Mục tiêu giao cho orchestrator
            </label>
            <textarea
              className="w-full rounded-lg border border-outline bg-surface-container-lowest p-3 text-body-md text-on-surface focus:border-primary focus:outline-none"
              id="schedule-goal"
              onChange={(event) => setGoalTemplate(event.target.value)}
              placeholder="vd: Quét xu hướng tuần này, chọn 3 chủ đề và soạn bài Facebook + Instagram cho từng chủ đề."
              rows={4}
              value={goalTemplate}
            />
          </div>
          <div>
            <p className="mb-1 text-body-md font-bold text-secondary">Kích hoạt theo</p>
            <div className="flex gap-4 text-body-md text-on-surface">
              <label className="flex items-center gap-2">
                <input checked={triggerType === "cadence"} name="trigger-type" onChange={() => setTriggerType("cadence")} type="radio" />
                Lịch định kỳ
              </label>
              <label className="flex items-center gap-2">
                <input checked={triggerType === "event"} name="trigger-type" onChange={() => setTriggerType("event")} type="radio" />
                Sự kiện hệ thống
              </label>
            </div>
          </div>
          <div className="max-w-xs">
            {triggerType === "cadence" ? (
              <div>
                <label className="mb-1 block text-body-md font-bold text-secondary" htmlFor="schedule-cadence">
                  Chu kỳ
                </label>
                <select className={SELECT_CLASS} id="schedule-cadence" onChange={(event) => setCadence(event.target.value)} value={cadence}>
                  {CADENCE_OPTIONS.map((option) => (
                    <option key={option} value={option}>
                      {CADENCE_LABELS[option]}
                    </option>
                  ))}
                </select>
              </div>
            ) : (
              <div>
                <label className="mb-1 block text-body-md font-bold text-secondary" htmlFor="schedule-event">
                  Sự kiện kích hoạt
                </label>
                <select className={SELECT_CLASS} id="schedule-event" onChange={(event) => setEventKey(event.target.value)} value={eventKey}>
                  {EVENT_OPTIONS.map((option) => (
                    <option key={option.value} value={option.value}>
                      {option.label}
                    </option>
                  ))}
                </select>
              </div>
            )}
          </div>
          <p className="text-label-sm text-on-surface-variant">
            {triggerType === "cadence"
              ? "Múi giờ Việt Nam (Asia/Ho_Chi_Minh). Lịch chạy lần đầu ngay sau khi tạo, sau đó lặp theo chu kỳ."
              : "Lịch sẽ ngủ và tự chạy mỗi khi hệ thống phát sự kiện đã chọn (vd. sau mỗi lần quét xu hướng)."}
          </p>
        </div>
      </Modal>

      <ConfirmDialog
        confirmLabel="Xóa lịch"
        message={scheduleToDelete ? `Xóa lịch tự động “${scheduleToDelete.name}”? Lịch sử các lần chạy vẫn được giữ lại.` : ""}
        onCancel={() => setScheduleToDelete(null)}
        onConfirm={() => {
          if (scheduleToDelete) deleteMutation.mutate(scheduleToDelete.id);
        }}
        open={scheduleToDelete !== null}
        pending={deleteMutation.isPending}
        title="Xóa lịch tự động"
      />
    </Card>
  );
}
