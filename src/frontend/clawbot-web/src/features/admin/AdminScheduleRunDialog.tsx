import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { Alert, Button, Modal, StatusPill } from "@/shared/ui";
import { getAdminScheduleRun } from "@/shared/api/admin";
import { toUserFriendlyError } from "@/shared/utils/userText";
import { formatDateTime } from "./adminHelpers";

interface AdminScheduleRunDialogProps {
  readonly runId: string | null;
  readonly onClose: () => void;
}

function isActive(status: string | undefined): boolean {
  return status === undefined || status === "started";
}

function statusTone(status: string): "success" | "error" | "warning" | "neutral" {
  if (status === "completed") return "success";
  if (status === "failed") return "error";
  if (status === "started") return "warning";
  return "neutral";
}

function statusLabel(status: string): string {
  if (status === "started") return "Đang chạy";
  if (status === "completed") return "Hoàn tất";
  if (status === "failed") return "Thất bại";
  if (status === "cancelled") return "Đã hủy";
  if (status === "skipped_overlap") return "Bỏ qua do trùng lịch";
  return status;
}

function RunDetail({ label, value }: { readonly label: string; readonly value: string }) {
  return (
    <div className="flex justify-between gap-4 border-b border-outline-variant py-2 last:border-0">
      <dt className="text-label-sm text-on-surface-variant">{label}</dt>
      <dd className="text-right text-body-sm text-on-surface">{value}</dd>
    </div>
  );
}

export function AdminScheduleRunDialog({ runId, onClose }: AdminScheduleRunDialogProps) {
  const runQuery = useQuery({
    queryKey: ["admin", "jobs", "schedule-run", runId],
    queryFn: () => getAdminScheduleRun(runId!),
    enabled: runId !== null,
    refetchInterval: (query) => (isActive(query.state.data?.status) ? 2_000 : false),
  });

  const run = runQuery.data;

  return (
    <Modal
      open={runId !== null}
      onClose={onClose}
      title="Theo dõi lần chạy lịch"
      maxWidthClass="max-w-2xl"
      footer={<Button variant="outline" onClick={onClose}>Đóng</Button>}
    >
      <p className="text-body-md text-on-surface-variant">
        Trạng thái được lấy từ lần chạy lịch thực tế, không phải chỉ từ xác nhận gửi yêu cầu.
      </p>
      {runQuery.error ? <Alert tone="error">{toUserFriendlyError(runQuery.error)}</Alert> : null}
      {runQuery.isLoading ? <p className="text-body-md text-on-surface-variant">Đang tải trạng thái lần chạy.</p> : null}
      {run ? (
        <div className="space-y-5">
          <section className="flex items-center justify-between gap-4 rounded-lg border border-outline-variant bg-surface-container-low p-4">
            <div>
              <p className="text-label-sm text-on-surface-variant">Lịch {run.scheduleId}</p>
              {run.sessionId ? (
                <Link
                  to={`/agents/runs/${encodeURIComponent(run.sessionId)}`}
                  className="mt-1 inline-flex text-body-sm text-primary underline"
                >
                  Mở phiên điều phối
                </Link>
              ) : (
                <p className="mt-1 text-body-sm text-on-surface-variant">Phiên đang được tạo hoặc không áp dụng.</p>
              )}
            </div>
            <StatusPill tone={statusTone(run.status)}>{statusLabel(run.status)}</StatusPill>
          </section>
          <dl className="rounded-lg border border-outline-variant px-4">
            <RunDetail label="Bắt đầu" value={formatDateTime(run.startedAt)} />
            <RunDetail label="Nhịp tim gần nhất" value={run.lastHeartbeatAt ? formatDateTime(run.lastHeartbeatAt) : "—"} />
            <RunDetail label="Kết thúc" value={run.finishedAt ? formatDateTime(run.finishedAt) : "Chưa kết thúc"} />
          </dl>
          {run.error ? <Alert tone="error">{run.error}</Alert> : null}
        </div>
      ) : null}
    </Modal>
  );
}
