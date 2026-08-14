import { useMutation, useQuery } from "@tanstack/react-query";
import { Alert, Button, Modal, StatusPill } from "@/shared/ui";
import {
  getAdminRecurringExecution,
  getAdminRecurringExecutionAttempts,
  retryAdminRecurringExecution,
  type RecurringExecutionStatus,
} from "@/shared/api/admin";
import { toUserFriendlyError } from "@/shared/utils/userText";
import { formatDateTime } from "./adminHelpers";

interface AdminRecurringExecutionDialogProps {
  readonly executionId: string | null;
  readonly onClose: () => void;
  readonly onExecutionTracked: (executionId: string) => void;
}

const TERMINAL_STATUSES = new Set<RecurringExecutionStatus>([
  "succeeded",
  "failed",
  "cancelled",
  "skipped",
  "enqueue_failed",
]);

const STATUS_LABELS: Record<RecurringExecutionStatus, string> = {
  requested: "Đã yêu cầu",
  queued: "Đã xếp hàng",
  running: "Đang chạy",
  retrying: "Đang thử lại",
  succeeded: "Hoàn tất",
  failed: "Thất bại",
  cancelled: "Đã hủy",
  skipped: "Đã bỏ qua",
  enqueue_failed: "Không thể xếp hàng",
};

function isTerminal(status: RecurringExecutionStatus | undefined): boolean {
  return status !== undefined && TERMINAL_STATUSES.has(status);
}

function statusTone(status: RecurringExecutionStatus): "success" | "error" | "warning" | "neutral" {
  if (status === "succeeded") return "success";
  if (status === "failed" || status === "enqueue_failed") return "error";
  if (status === "running" || status === "retrying") return "warning";
  return "neutral";
}

function sourceLabel(source: "scheduled" | "manual" | "manual_retry"): string {
  if (source === "scheduled") return "Theo lịch";
  if (source === "manual_retry") return "Chạy lại thủ công";
  return "Chạy thủ công";
}

function DetailRow({ label, value }: { readonly label: string; readonly value: string }) {
  return (
    <div className="flex justify-between gap-4 border-b border-outline-variant py-2 last:border-0">
      <dt className="text-label-sm text-on-surface-variant">{label}</dt>
      <dd className="text-right text-body-sm text-on-surface">{value}</dd>
    </div>
  );
}

export function AdminRecurringExecutionDialog({
  executionId,
  onClose,
  onExecutionTracked,
}: AdminRecurringExecutionDialogProps) {
  const executionQuery = useQuery({
    queryKey: ["admin", "jobs", "execution", executionId],
    queryFn: () => getAdminRecurringExecution(executionId!),
    enabled: executionId !== null,
    refetchInterval: (query) => (isTerminal(query.state.data?.status) ? false : 2_000),
  });
  const attemptsQuery = useQuery({
    queryKey: ["admin", "jobs", "execution", executionId, "attempts"],
    queryFn: () => getAdminRecurringExecutionAttempts(executionId!),
    enabled: executionId !== null,
    refetchInterval: () => (isTerminal(executionQuery.data?.status) ? false : 2_000),
  });
  const retryMutation = useMutation({
    mutationFn: retryAdminRecurringExecution,
    onSuccess: (response) => onExecutionTracked(response.trackingId),
  });

  const execution = executionQuery.data;
  const error = executionQuery.error ?? attemptsQuery.error ?? retryMutation.error;
  const canRetry = execution?.status === "failed" || execution?.status === "enqueue_failed";

  return (
    <Modal
      open={executionId !== null}
      onClose={onClose}
      title="Theo dõi thực thi job"
      maxWidthClass="max-w-4xl"
      footer={
        <>
          <Button variant="outline" onClick={onClose}>Đóng</Button>
          {canRetry ? (
            <Button
              disabled={retryMutation.isPending}
              onClick={() => retryMutation.mutate(execution.id)}
            >
              {retryMutation.isPending ? "Đang tạo lần chạy lại" : "Chạy lại"}
            </Button>
          ) : null}
        </>
      }
    >
      <p className="text-body-md text-on-surface-variant">
        Trạng thái Hangfire chỉ xác nhận việc giao tác vụ. Bảng này phản ánh tiến trình và kết quả nghiệp vụ đã được theo dõi bền vững.
      </p>

      {error ? <Alert tone="error">{toUserFriendlyError(error)}</Alert> : null}
      {executionQuery.isLoading ? <p className="text-body-md text-on-surface-variant">Đang tải trạng thái thực thi.</p> : null}

      {execution ? (
        <div className="space-y-6">
          <section aria-label="Trạng thái thực thi" className="rounded-lg border border-outline-variant bg-surface-container-low p-4">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <p className="text-label-sm text-on-surface-variant">{execution.definitionId}</p>
                <p className="mt-1 text-body-sm text-on-surface-variant">{sourceLabel(execution.source)}</p>
                {execution.retryOfExecutionId ? (
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => onExecutionTracked(execution.retryOfExecutionId!)}
                  >
                    Xem lần chạy gốc
                  </Button>
                ) : null}
              </div>
              <StatusPill tone={statusTone(execution.status)}>{STATUS_LABELS[execution.status]}</StatusPill>
            </div>
            <div className="mt-4">
              <div className="mb-2 flex justify-between gap-3 text-label-sm text-on-surface-variant">
                <span>Tiến độ nghiệp vụ</span>
                <span>{execution.progressPercent}%</span>
              </div>
              <div className="h-2 overflow-hidden rounded-full bg-surface-variant">
                <div
                  className="h-full origin-left rounded-full bg-primary transition-transform duration-300"
                  style={{ transform: `scaleX(${execution.progressPercent / 100})` }}
                  role="progressbar"
                  aria-label="Tiến độ thực thi"
                  aria-valuemin={0}
                  aria-valuemax={100}
                  aria-valuenow={execution.progressPercent}
                />
              </div>
              {execution.progressNote ? <p className="mt-2 text-body-sm text-on-surface-variant">{execution.progressNote}</p> : null}
            </div>
          </section>

          <section aria-label="Mốc thời gian">
            <h4 className="text-title-sm text-on-surface">Mốc thời gian</h4>
            <dl className="mt-2 rounded-lg border border-outline-variant px-4">
              <DetailRow label="Yêu cầu" value={formatDateTime(execution.requestedAt)} />
              <DetailRow label="Đã xếp hàng" value={execution.enqueuedAt ? formatDateTime(execution.enqueuedAt) : "Đang chờ"} />
              <DetailRow label="Bắt đầu" value={execution.startedAt ? formatDateTime(execution.startedAt) : "Chưa bắt đầu"} />
              <DetailRow label="Kết thúc" value={execution.finishedAt ? formatDateTime(execution.finishedAt) : "Chưa kết thúc"} />
            </dl>
          </section>

          {execution.resultSummary || execution.error ? (
            <section aria-label="Kết quả thực thi" className="rounded-lg border border-outline-variant p-4">
              <h4 className="text-title-sm text-on-surface">Kết quả</h4>
              {execution.resultSummary ? <p className="mt-2 text-body-md text-on-surface">{execution.resultSummary}</p> : null}
              {execution.error ? <Alert tone="error">{execution.error}</Alert> : null}
              {execution.resultLink ? (
                <a className="mt-3 inline-flex text-label-md text-primary underline" href={execution.resultLink}>Xem kết quả</a>
              ) : null}
            </section>
          ) : null}

          <section aria-labelledby="attempt-history-heading">
            <div className="flex items-center justify-between gap-4">
              <h4 id="attempt-history-heading" className="text-title-sm text-on-surface">Lần thực thi</h4>
              {attemptsQuery.data?.total !== null && attemptsQuery.data?.total !== undefined ? (
                <span className="text-label-sm text-on-surface-variant">{attemptsQuery.data.total} lần</span>
              ) : null}
            </div>
            <div className="mt-2 overflow-x-auto rounded-lg border border-outline-variant">
              <table className="w-full text-left text-body-sm">
                <thead className="bg-surface-container-low text-label-sm text-on-surface-variant">
                  <tr>
                    <th className="px-3 py-2">Lần</th>
                    <th className="px-3 py-2">Trạng thái</th>
                    <th className="px-3 py-2">Bắt đầu</th>
                    <th className="px-3 py-2">Kết thúc</th>
                    <th className="px-3 py-2">Thông tin an toàn</th>
                  </tr>
                </thead>
                <tbody>
                  {attemptsQuery.data?.items.map((attempt) => (
                    <tr key={attempt.id} className="border-t border-outline-variant">
                      <td className="px-3 py-2">{attempt.attemptNumber}</td>
                      <td className="px-3 py-2">{attempt.status}</td>
                      <td className="px-3 py-2">{formatDateTime(attempt.startedAt)}</td>
                      <td className="px-3 py-2">{attempt.finishedAt ? formatDateTime(attempt.finishedAt) : "Đang chạy"}</td>
                      <td className="max-w-[280px] px-3 py-2 text-on-surface-variant">{attempt.error ?? "—"}</td>
                    </tr>
                  ))}
                  {!attemptsQuery.isLoading && !attemptsQuery.data?.items.length ? (
                    <tr><td colSpan={5} className="px-3 py-4 text-center text-on-surface-variant">Chưa có lần thực thi nào.</td></tr>
                  ) : null}
                </tbody>
              </table>
            </div>
          </section>
        </div>
      ) : null}
    </Modal>
  );
}
