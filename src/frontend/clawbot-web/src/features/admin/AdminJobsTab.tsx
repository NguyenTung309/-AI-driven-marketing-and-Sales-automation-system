import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Alert, Button, Card, StatusPill } from "@/shared/ui";
import { toUserFriendlyError } from "@/shared/utils/userText";
import {
  activateAdminScheduleJob,
  getAdminJobs,
  pauseAdminScheduleJob,
  runAdminScheduleJobNow,
  triggerAdminRecurringJob,
  type AdminRecurringJob,
  type AdminScheduleJob,
} from "@/shared/api/admin";
import { formatDateTime } from "./adminHelpers";
import { EmptyState } from "./adminUi";
import { AdminRecurringExecutionDialog } from "./AdminRecurringExecutionDialog";
import { AdminScheduleRunDialog } from "./AdminScheduleRunDialog";

const CADENCE_LABELS: Record<string, string> = {
  daily: "Hằng ngày",
  weekly: "Hằng tuần",
  monthly: "Hằng tháng",
  quarterly: "Hằng quý",
};

function cadenceLabel(cadence: string): string {
  return CADENCE_LABELS[cadence.toLowerCase()] ?? cadence;
}

function agentPill(agent?: string | null) {
  return agent ? (
    <StatusPill tone="success">{agent}</StatusPill>
  ) : (
    <span className="text-body-md text-on-surface-variant">—</span>
  );
}

export function AdminJobsTab() {
  const queryClient = useQueryClient();
  const [trackedExecutionId, setTrackedExecutionId] = useState<string | null>(null);
  const [trackedScheduleRunId, setTrackedScheduleRunId] = useState<string | null>(null);
  const jobsQuery = useQuery({ queryKey: ["admin", "jobs"], queryFn: getAdminJobs, refetchInterval: 30_000 });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["admin", "jobs"] });
  const triggerMutation = useMutation({
    mutationFn: triggerAdminRecurringJob,
    onSuccess: async (response) => {
      setTrackedExecutionId(response.trackingId);
      await invalidate();
    },
  });
  const runNowMutation = useMutation({
    mutationFn: runAdminScheduleJobNow,
    onSuccess: async (response) => {
      setTrackedScheduleRunId(response.runId);
      await invalidate();
    },
  });
  const pauseMutation = useMutation({ mutationFn: pauseAdminScheduleJob, onSuccess: invalidate });
  const activateMutation = useMutation({ mutationFn: activateAdminScheduleJob, onSuccess: invalidate });

  const error =
    jobsQuery.error ?? triggerMutation.error ?? runNowMutation.error ?? pauseMutation.error ?? activateMutation.error;

  const recurring = jobsQuery.data?.recurring ?? [];
  const schedules = jobsQuery.data?.schedules ?? [];

  return (
    <div className="space-y-gutter">
      {error ? <Alert tone="error">{toUserFriendlyError(error)}</Alert> : null}

      {/* <Card>
        <div className="mb-4">
          <h2 className="text-headline-sm text-secondary">Job hệ thống (Hangfire)</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">
            Job nền chạy theo cron cho toàn hệ thống. Cột Agent cho biết job gọi agent nào qua gRPC.
          </p>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-body-md">
            <thead>
              <tr className="text-label-sm text-on-surface-variant">
                <th className="px-3 py-2">Job</th>
                <th className="px-3 py-2">Cron</th>
                <th className="px-3 py-2">Hàng đợi</th>
                <th className="px-3 py-2">Agent</th>
                <th className="px-3 py-2">Hangfire gần nhất</th>
                <th className="px-3 py-2">Kế tiếp</th>
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody>
              {recurring.map((job: AdminRecurringJob) => (
                <tr key={job.id} className="border-t border-outline hover:bg-surface-container-low">
                  <td className="px-3 py-3">
                    <p className="font-semibold text-secondary">{job.id}</p>
                    {job.description ? <p className="text-label-sm text-on-surface-variant">{job.description}</p> : null}
                  </td>
                  <td className="px-3 py-3 font-mono text-mono-status text-on-surface-variant">{job.cron}</td>
                  <td className="px-3 py-3 text-on-surface-variant">{job.queue}</td>
                  <td className="px-3 py-3">{agentPill(job.agent)}</td>
                  <td className="px-3 py-3 text-on-surface-variant">
                    {job.lastExecution ? formatDateTime(job.lastExecution) : "—"}
                    {job.lastState ? (
                      <span className="ml-2">
                        <StatusPill tone={job.lastState === "Succeeded" ? "success" : job.lastState === "Failed" ? "error" : "neutral"}>
                          {job.lastState}
                        </StatusPill>
                      </span>
                    ) : null}
                  </td>
                  <td className="px-3 py-3 text-on-surface-variant">{job.nextExecution ? formatDateTime(job.nextExecution) : "—"}</td>
                  <td className="px-3 py-3">
                    <div className="flex flex-wrap gap-2">
                      {job.latestExecution ? (
                        <Button
                          size="sm"
                          variant="outline"
                          onClick={() => setTrackedExecutionId(job.latestExecution!.id)}
                        >
                          Theo dõi lần gần nhất
                        </Button>
                      ) : null}
                      {job.canTriggerManually ? (
                        <Button size="sm" variant="outline" disabled={triggerMutation.isPending} onClick={() => triggerMutation.mutate(job.id)}>
                          Chạy ngay
                        </Button>
                      ) : (
                        <span className="self-center text-label-sm text-on-surface-variant">Chưa hỗ trợ</span>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {!jobsQuery.isLoading && !recurring.length ? <EmptyState>Chưa có job hệ thống nào được đăng ký.</EmptyState> : null}
        </div>
      </Card> */}

      <Card>
        <div className="mb-4">
          <h2 className="text-headline-sm text-secondary">Lịch agent (OrchestrationV2)</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">
            Lịch của tenant: quét xu hướng chạy thẳng research-agent; các lịch khác chạy qua orchestrator (LLM tự chọn agent theo mục tiêu).
          </p>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-body-md">
            <thead>
              <tr className="text-label-sm text-on-surface-variant">
                <th className="px-3 py-2">Tên lịch</th>
                <th className="px-3 py-2">Loại</th>
                <th className="px-3 py-2">Agent</th>
                <th className="px-3 py-2">Chu kỳ</th>
                <th className="px-3 py-2">Kế tiếp</th>
                <th className="px-3 py-2">Lần cuối</th>
                <th className="px-3 py-2">Trạng thái</th>
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody>
              {schedules.map((schedule: AdminScheduleJob) => (
                <tr key={schedule.id} className="border-t border-outline hover:bg-surface-container-low">
                  <td className="px-3 py-3">
                    <p className="font-semibold text-secondary">{schedule.name}</p>
                    <p className="max-w-[280px] truncate text-label-sm text-on-surface-variant" title={schedule.goalTemplate}>
                      {schedule.goalTemplate}
                    </p>
                  </td>
                  <td className="px-3 py-3">
                    <StatusPill tone={schedule.kind === "trend-scan" ? "warning" : "neutral"}>
                      {schedule.kind === "trend-scan" ? "Quét xu hướng" : "Orchestration"}
                    </StatusPill>
                  </td>
                  <td className="px-3 py-3">
                    {schedule.agent ? agentPill(schedule.agent) : (
                      <span className="text-label-sm text-on-surface-variant">Orchestrator tự chọn</span>
                    )}
                  </td>
                  <td className="px-3 py-3 text-on-surface-variant">{cadenceLabel(schedule.cadence)}</td>
                  <td className="px-3 py-3 text-on-surface-variant">{formatDateTime(schedule.nextRunAt)}</td>
                  <td className="px-3 py-3 text-on-surface-variant">{schedule.lastRunAt ? formatDateTime(schedule.lastRunAt) : "—"}</td>
                  <td className="px-3 py-3">
                    <StatusPill tone={schedule.isActive ? "success" : "neutral"}>
                      {schedule.isActive ? "Đang bật" : "Tạm dừng"}
                    </StatusPill>
                  </td>
                  <td className="px-3 py-3">
                    <div className="flex gap-2">
                      <Button
                        size="sm"
                        variant="outline"
                        disabled={runNowMutation.isPending && runNowMutation.variables === schedule.id}
                        onClick={() => runNowMutation.mutate(schedule.id)}
                      >
                        Chạy ngay
                      </Button>
                      {schedule.isActive ? (
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={pauseMutation.isPending && pauseMutation.variables === schedule.id}
                          onClick={() => pauseMutation.mutate(schedule.id)}
                        >
                          Tạm dừng
                        </Button>
                      ) : (
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={activateMutation.isPending && activateMutation.variables === schedule.id}
                          onClick={() => activateMutation.mutate(schedule.id)}
                        >
                          Bật lại
                        </Button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {!jobsQuery.isLoading && !schedules.length ? <EmptyState>Chưa có lịch agent nào.</EmptyState> : null}
        </div>
      </Card>
      <AdminRecurringExecutionDialog
        executionId={trackedExecutionId}
        onClose={() => setTrackedExecutionId(null)}
        onExecutionTracked={setTrackedExecutionId}
      />
      <AdminScheduleRunDialog
        runId={trackedScheduleRunId}
        onClose={() => setTrackedScheduleRunId(null)}
      />
    </div>
  );
}
