import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { AppShell } from "@/shared/layout/AppShell";
import { Alert, Button, Card, StatusPill } from "@/shared/ui";
import { useAuthStore } from "@/shared/auth/authStore";
import { statusLabel, statusTone } from "./orchestrationStatus";
import { RunCompareDialog } from "./RunCompareDialog";
import { useOrchestrationRealtime } from "./useOrchestrationRealtime";
import { useRunControls } from "./useRunControls";
import {
  archiveOrchestrationV2Run,
  getOrchestrationV2Run,
  listOrchestrationV2Runs,
  unarchiveOrchestrationV2Run,
  type OrchestrationV2RunSummary,
} from "@/shared/api/orchestrationV2";

type RunFilter = "all" | "mine" | "pending" | "archived";

const FILTERS: readonly { readonly key: RunFilter; readonly label: string }[] = [
  { key: "all", label: "Tất cả" },
  { key: "mine", label: "Của tôi" },
  { key: "pending", label: "Chờ phê duyệt" },
  { key: "archived", label: "Đã ẩn" },
];

function durationLabel(run: OrchestrationV2RunSummary): string {
  if (!run.finishedAt) return "—";
  const ms = new Date(run.finishedAt).getTime() - new Date(run.startedAt).getTime();
  if (!Number.isFinite(ms) || ms < 0) return "—";
  const seconds = Math.round(ms / 1000);
  if (seconds < 60) return `${seconds} giây`;
  return `${Math.round(seconds / 60)} phút`;
}

// B3: bảng phiên thường trực + hàng đợi phê duyệt — thay vì kẹt trong modal "Phiên gần đây".
export default function AgentRunsPage() {
  const queryClient = useQueryClient();
  const permissions = useAuthStore((s) => s.permissions);
  const canApprove = permissions.includes("orchestration:approve");
  const canManage = permissions.includes("orchestration:manage");
  const [filter, setFilter] = useState<RunFilter>("all");
  // Selection đa dụng: tick nhiều phiên để ẩn hàng loạt; "So sánh" yêu cầu đúng 2 phiên được tick.
  const [selection, setSelection] = useState<readonly string[]>([]);
  const [compareIds, setCompareIds] = useState<readonly [string, string] | null>(null);

  function toggleSelect(sessionId: string) {
    setSelection((current) =>
      current.includes(sessionId)
        ? current.filter((id) => id !== sessionId)
        : [...current, sessionId],
    );
  }

  const live = useOrchestrationRealtime(true) === "connected";
  const runsQuery = useQuery({
    queryKey: ["orchestration", "runs", filter],
    queryFn: () => listOrchestrationV2Runs(filter === "mine", filter === "archived"),
    refetchInterval: live ? 30_000 : 5_000,
  });
  const controls = useRunControls(null);
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["orchestration", "runs"] });
  const archiveMutation = useMutation({ mutationFn: archiveOrchestrationV2Run, onSuccess: invalidate });
  const unarchiveMutation = useMutation({ mutationFn: unarchiveOrchestrationV2Run, onSuccess: invalidate });
  // Ẩn hàng loạt các phiên đã tick (chỉ phiên đã kết thúc — completed/failed/cancelled).
  const bulkArchiveMutation = useMutation({
    mutationFn: async (ids: readonly string[]) => {
      for (const id of ids) await archiveOrchestrationV2Run(id);
      return ids.length;
    },
    onSuccess: () => {
      setSelection([]);
      invalidate();
    },
  });

  const busy = controls.busy || archiveMutation.isPending || unarchiveMutation.isPending || bulkArchiveMutation.isPending;
  const error = runsQuery.error ?? controls.error ?? archiveMutation.error ?? unarchiveMutation.error ?? bulkArchiveMutation.error;

  const allRuns = runsQuery.data ?? [];
  // Run chờ duyệt ghim lên đầu ở mọi filter (trừ khi đang xem "Đã ẩn").
  const runs = (filter === "pending" ? allRuns.filter((run) => run.status === "pending_approval") : allRuns)
    .slice()
    .sort((a, b) => {
      const pendingDelta = Number(b.status === "pending_approval") - Number(a.status === "pending_approval");
      return pendingDelta !== 0 ? pendingDelta : b.startedAt.localeCompare(a.startedAt);
    });
  const pendingCount = allRuns.filter((run) => run.status === "pending_approval").length;
  const isArchivable = (run: OrchestrationV2RunSummary) =>
    run.status === "completed" || run.status === "failed" || run.status === "cancelled";
  const archivableSelectedIds = runs
    .filter((run) => selection.includes(run.sessionId) && isArchivable(run))
    .map((run) => run.sessionId);
  const allDisplayedSelected = runs.length > 0 && runs.every((run) => selection.includes(run.sessionId));

  function toggleSelectAll() {
    setSelection(allDisplayedSelected ? [] : runs.map((run) => run.sessionId));
  }

  return (
    <AppShell title="Phiên điều phối">
      <div className="mb-stack-lg flex flex-col gap-2">
        <Link className="text-label-sm text-primary hover:underline" to="/agents">
          ← Quay lại Giám sát Agent
        </Link>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-display-lg font-black text-on-surface">Phiên điều phối</h1>
          {pendingCount > 0 && filter !== "archived" ? (
            <StatusPill tone="warning">{pendingCount} phiên chờ phê duyệt</StatusPill>
          ) : null}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {FILTERS.map((item) => (
            <button
              className={[
                "rounded-full border px-3 py-1 text-label-sm transition-colors",
                filter === item.key
                  ? "border-primary bg-primary/10 text-primary"
                  : "border-outline bg-surface-container-lowest text-secondary hover:border-primary hover:text-primary",
              ].join(" ")}
              key={item.key}
              onClick={() => setFilter(item.key)}
              type="button"
            >
              {item.label}
            </button>
          ))}
          <Button
            disabled={selection.length !== 2}
            onClick={() => setCompareIds([selection[0], selection[1]])}
            size="sm"
            title="Tick đúng 2 phiên trong bảng để so sánh"
            variant="outline"
          >
            So sánh ({Math.min(selection.length, 2)}/2)
          </Button>
          {filter !== "archived" ? (
            <Button
              disabled={!canManage || busy || archivableSelectedIds.length === 0}
              onClick={() => {
                if (window.confirm(`Ẩn ${archivableSelectedIds.length} phiên đã chọn?`))
                  bulkArchiveMutation.mutate(archivableSelectedIds);
              }}
              size="sm"
              title={!canManage
                ? "Cần quyền orchestration:manage"
                : "Chỉ ẩn được phiên đã kết thúc (Hoàn tất / Thất bại / Đã hủy) trong số đã tick"}
              variant="ghost"
            >
              {bulkArchiveMutation.isPending ? "Đang ẩn..." : `Ẩn đã chọn (${archivableSelectedIds.length})`}
            </Button>
          ) : null}
        </div>
      </div>

      {error ? (
        <div className="mb-gutter">
          <Alert tone="error">{error instanceof Error ? error.message : "Đã xảy ra lỗi không xác định."}</Alert>
        </div>
      ) : null}

      <Card>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-body-md">
            <thead>
              <tr className="text-label-sm text-on-surface-variant">
                <th className="w-8 px-3 py-2">
                  <input
                    aria-label="Chọn tất cả phiên đang hiển thị"
                    checked={allDisplayedSelected}
                    onChange={toggleSelectAll}
                    title="Chọn tất cả"
                    type="checkbox"
                  />
                </th>
                <th className="px-3 py-2">Trạng thái</th>
                <th className="px-3 py-2">Mục tiêu</th>
                <th className="px-3 py-2">Bắt đầu</th>
                <th className="px-3 py-2">Thời lượng</th>
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody>
              {runs.map((run) => (
                <tr className="border-t border-outline hover:bg-surface-container-low" key={run.sessionId}>
                  <td className="px-3 py-3">
                    <input
                      aria-label="Chọn phiên"
                      checked={selection.includes(run.sessionId)}
                      onChange={() => toggleSelect(run.sessionId)}
                      type="checkbox"
                    />
                  </td>
                  <td className="px-3 py-3">
                    <StatusPill tone={statusTone(run.status)}>{statusLabel(run.status)}</StatusPill>
                  </td>
                  <td className="max-w-[420px] px-3 py-3">
                    <Link
                      className="block truncate text-on-surface hover:text-primary hover:underline"
                      title={run.goal ?? ""}
                      to={`/agents/runs/${encodeURIComponent(run.sessionId)}`}
                    >
                      {run.goal || "(không có mục tiêu)"}
                    </Link>
                  </td>
                  <td className="px-3 py-3 text-on-surface-variant">{new Date(run.startedAt).toLocaleString("vi-VN")}</td>
                  <td className="px-3 py-3 text-on-surface-variant">{durationLabel(run)}</td>
                  <td className="px-3 py-3">
                    <div className="flex flex-wrap justify-end gap-2">
                      {run.status === "pending_approval" ? (
                        <Button
                          disabled={!canApprove || busy}
                          onClick={async () => {
                            const detail = await getOrchestrationV2Run(run.sessionId);
                            controls.approve.mutate({ sessionId: run.sessionId, etag: detail.etag });
                          }}
                          size="sm"
                          title={!canApprove ? "Cần quyền orchestration:approve" : undefined}
                        >
                          Phê duyệt
                        </Button>
                      ) : null}
                      {run.status === "running" || run.status === "paused" ? (
                        <Button
                          disabled={!canManage || busy}
                          onClick={() => {
                            if (window.confirm("Hủy phiên này?")) controls.control.mutate({ action: "cancel", sessionId: run.sessionId });
                          }}
                          size="sm"
                          title={!canManage ? "Cần quyền orchestration:manage" : undefined}
                          variant="ghost"
                        >
                          Hủy
                        </Button>
                      ) : null}
                      {filter === "archived" ? (
                        <Button
                          disabled={!canManage || busy}
                          onClick={() => unarchiveMutation.mutate(run.sessionId)}
                          size="sm"
                          title={!canManage ? "Cần quyền orchestration:manage" : undefined}
                          variant="outline"
                        >
                          Khôi phục
                        </Button>
                      ) : run.status === "completed" || run.status === "failed" || run.status === "cancelled" ? (
                        <Button
                          disabled={!canManage || busy}
                          onClick={() => archiveMutation.mutate(run.sessionId)}
                          size="sm"
                          title={!canManage ? "Cần quyền orchestration:manage" : undefined}
                          variant="ghost"
                        >
                          Ẩn
                        </Button>
                      ) : null}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {!runsQuery.isLoading && !runs.length ? (
            <p className="p-4 text-body-md text-on-surface-variant">
              {filter === "pending" ? "Không có phiên nào chờ phê duyệt." : "Chưa có phiên nào."}
            </p>
          ) : null}
        </div>
      </Card>

      <RunCompareDialog ids={compareIds} onClose={() => setCompareIds(null)} />
    </AppShell>
  );
}
