import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { AppShell } from "@/shared/layout/AppShell";
import { Card } from "@/shared/ui/Card";
import { StatusPill, type StatusTone } from "@/shared/ui/StatusPill";
import { InfiniteScrollSentinel, useDebounce, useInfiniteList } from "@/shared/ui";
import { operationalPhaseLabel, toSafeOperationalText } from "@/shared/utils/userText";
import {
  getTaskRunDetail,
  listLogAudit,
  listTaskRuns,
  type AuditLogListResponse,
  type TaskRunAudit,
  type TaskRunListItem,
  type TaskRunListResponse,
  type TaskRunTrace,
} from "@/shared/api/logs";

const EMPTY_RUNS: readonly TaskRunListItem[] = [];
const EMPTY_TRACES: readonly TaskRunTrace[] = [];
const EMPTY_AUDIT: readonly TaskRunAudit[] = [];

function formatDateTime(value: string | null): string {
  if (!value) return "-";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("vi-VN", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

function formatDuration(ms: number): string {
  if (!Number.isFinite(ms) || ms <= 0) return "0 giây";
  const seconds = Math.round(ms / 1000);
  if (seconds < 60) return `${seconds} giây`;
  const minutes = Math.floor(seconds / 60);
  const rest = seconds % 60;
  if (minutes < 60) return rest ? `${minutes} phút ${rest} giây` : `${minutes} phút`;
  const hours = Math.floor(minutes / 60);
  const remainingMinutes = minutes % 60;
  return remainingMinutes ? `${hours} giờ ${remainingMinutes} phút` : `${hours} giờ`;
}

function formatUsd(value: number): string {
  return new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", maximumFractionDigits: 4 }).format(value);
}

function statusTone(status: string): StatusTone {
  const value = status.toLowerCase();
  if (value.includes("fail") || value.includes("error")) return "error";
  if (value.includes("running") || value.includes("pending")) return "warning";
  if (value.includes("complete") || value.includes("success")) return "success";
  return "neutral";
}

function statusLabel(status: string): string {
  const value = status.toLowerCase();
  if (value.includes("running")) return "Đang chạy";
  if (value.includes("pending")) return "Đang chờ";
  if (value.includes("complete") || value.includes("success")) return "Hoàn tất";
  if (value.includes("fail")) return "Thất bại";
  if (value.includes("error")) return "Lỗi";
  return status;
}

function shortId(value: string | null): string {
  return value ? value.slice(0, 8) : "-";
}

function StatCard({ icon, label, value }: { readonly icon: string; readonly label: string; readonly value: string }) {
  return (
    <Card>
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-label-caps uppercase text-on-surface-variant">{label}</p>
          <p className="mt-2 text-telemetry-data text-secondary">{value}</p>
        </div>
        <span aria-hidden="true" className="material-symbols-outlined text-[22px] text-on-surface-variant">{icon}</span>
      </div>
    </Card>
  );
}

function RunTable({
  runs,
  activeRunId,
  onSelect,
}: {
  readonly runs: readonly TaskRunListItem[];
  readonly activeRunId: string | null;
  readonly onSelect: (id: string) => void;
}) {
  if (!runs.length) {
    return (
      <div className="flex min-h-64 flex-col items-center justify-center p-card-padding text-center">
        <span aria-hidden="true" className="material-symbols-outlined mb-3 text-[40px] text-on-surface-variant">receipt_long</span>
        <h2 className="text-headline-sm font-bold text-secondary">Chưa có lượt chạy tác vụ</h2>
        <p className="mt-2 max-w-md text-body-md text-on-surface-variant">
          Khi agent xử lý tác vụ, nhật ký sẽ xuất hiện tại đây.
        </p>
      </div>
    );
  }

  return (
    <div className="overflow-x-auto">
      <table className="min-w-[920px] w-full border-collapse text-left">
        <thead className="bg-surface text-label-sm uppercase text-secondary">
          <tr>
            <th className="px-4 py-3 font-bold">Lượt chạy</th>
            <th className="px-4 py-3 font-bold">Agent</th>
            <th className="px-4 py-3 font-bold">Mục tiêu</th>
            <th className="px-4 py-3 font-bold">Trạng thái</th>
            <th className="px-4 py-3 font-bold">Thời lượng</th>
            <th className="px-4 py-3 font-bold">Sự kiện</th>
            <th className="px-4 py-3 text-right font-bold">Chi phí</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-outline bg-white">
          {runs.map((run) => (
            <tr
              className={activeRunId === run.id ? "bg-primary/5" : "hover:bg-surface-container-low"}
              key={run.id}
            >
              <td className="px-4 py-4">
                <button
                  className="font-mono text-mono-status font-bold text-primary hover:underline"
                  onClick={() => onSelect(run.id)}
                  type="button"
                >
                  {shortId(run.id)}
                </button>
                <p className="mt-1 text-label-sm text-on-surface-variant">{formatDateTime(run.startedAt)}</p>
              </td>
              <td className="px-4 py-4">
                <p className="font-semibold text-secondary">{run.agentName}</p>
                <p className="font-mono text-mono-status text-on-surface-variant">{run.agentCode ?? run.agentType}</p>
              </td>
              <td className="max-w-[280px] px-4 py-4 text-body-md text-secondary">
                <p className="line-clamp-2">{run.goal}</p>
                {run.lastMessage ? <p className="mt-1 line-clamp-1 text-label-sm text-on-surface-variant">{run.lastMessage}</p> : null}
              </td>
              <td className="px-4 py-4">
                <StatusPill tone={statusTone(run.status)}>{statusLabel(run.status)}</StatusPill>
              </td>
              <td className="px-4 py-4 font-mono text-mono-status text-secondary">{formatDuration(run.durationMs)}</td>
              <td className="px-4 py-4">
                <p className="font-mono text-mono-status text-secondary">{run.traceCount}</p>
                <p className="text-label-sm text-on-surface-variant">{run.lastPhase ?? "Chưa có giai đoạn"}</p>
              </td>
              <td className="px-4 py-4 text-right">
                <p className="font-mono text-mono-status text-secondary">{run.totalTokens.toLocaleString("vi-VN")} lượt dùng</p>
                <p className="text-label-sm text-on-surface-variant">{formatUsd(run.usd)}</p>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function TraceList({ traces }: { readonly traces: readonly TaskRunTrace[] }) {
  if (!traces.length) return <p className="text-body-md text-on-surface-variant">Lượt chạy này chưa có sự kiện vận hành.</p>;

  return (
    <div className="space-y-3">
      {traces.map((trace) => (
        <div className="rounded border border-outline bg-surface p-3" key={trace.id}>
          <div className="mb-1 flex flex-wrap items-center justify-between gap-2">
            <span className="rounded bg-surface-container px-2 py-1 font-mono text-mono-status text-secondary">{operationalPhaseLabel(trace.phase)}</span>
            <span className="text-label-sm text-on-surface-variant">{formatDateTime(trace.occurredAt)}</span>
          </div>
          <p className="text-body-md text-secondary">{toSafeOperationalText(trace.message)}</p>
          <p className="mt-1 font-mono text-mono-status text-on-surface-variant">{trace.agentName}</p>
        </div>
      ))}
    </div>
  );
}

function AuditList({ events }: { readonly events: readonly TaskRunAudit[] }) {
  if (!events.length) return <p className="text-body-md text-on-surface-variant">Chưa có sự kiện quản trị gần đây.</p>;

  return (
    <div className="space-y-3">
      {events.map((event) => (
        <div className="rounded border border-outline bg-surface p-3" key={event.id}>
          <div className="mb-1 flex flex-wrap items-center justify-between gap-2">
            <span className="font-semibold text-secondary">{event.action}</span>
            <span className="text-label-sm text-on-surface-variant">{formatDateTime(event.occurredAt)}</span>
          </div>
          <p className="font-mono text-mono-status text-on-surface-variant">
            {event.resourceType} / {shortId(event.resourceId)}
          </p>
          {event.diffJson ? <p className="mt-2 line-clamp-2 text-body-md text-on-surface-variant">Đã ghi nhận thay đổi.</p> : null}
        </div>
      ))}
    </div>
  );
}

export default function TaskLogsPage() {
  const [status, setStatus] = useState("");
  const [query, setQuery] = useState("");
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);

  const debouncedQuery = useDebounce(query, 300);
  const runsList = useInfiniteList<TaskRunListItem, TaskRunListResponse>({
    queryKey: ["logs", "task-runs", status, debouncedQuery],
    queryFn: (pageParam) =>
      listTaskRuns({
        status: status || undefined,
        q: debouncedQuery || undefined,
        cursor: typeof pageParam === "string" ? pageParam : null,
        pageSize: 25,
      }),
  });
  const runsQuery = runsList.query;

  const runs = runsList.items.length ? runsList.items : EMPTY_RUNS;
  const activeRunId = selectedRunId ?? runs[0]?.id ?? null;

  const detailQuery = useQuery({
    queryKey: ["logs", "task-run-detail", activeRunId],
    queryFn: () => getTaskRunDetail(activeRunId as string),
    enabled: Boolean(activeRunId),
  });

  const auditList = useInfiniteList<TaskRunAudit, AuditLogListResponse>({
    queryKey: ["logs", "audit"],
    queryFn: (pageParam) =>
      listLogAudit({
        cursor: typeof pageParam === "string" ? pageParam : null,
        pageSize: 12,
      }),
  });
  const auditQuery = auditList.query;

  const stats = useMemo(() => {
    const pages = (runsQuery.data?.pages ?? []) as TaskRunListResponse[];
    for (const p of pages) {
      if (p.stats) return p.stats;
    }
    return undefined;
  }, [runsQuery.data]);
  const traces = detailQuery.data?.traces ?? EMPTY_TRACES;
  const runAudit = detailQuery.data?.auditEvents ?? EMPTY_AUDIT;
  const recentAudit = auditList.items.length ? auditList.items : EMPTY_AUDIT;

  const detailEvents = useMemo(() => {
    if (runAudit.length) return runAudit;
    return recentAudit.slice(0, 5);
  }, [recentAudit, runAudit]);

  return (
    <AppShell title="Nhật ký tác vụ">
      <section className="mb-gutter flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <h1 className="text-display-lg text-secondary">Nhật ký tác vụ</h1>
          <p className="mt-2 max-w-3xl text-body-md text-on-surface-variant">
            Theo dõi lượt chạy agent, sự kiện vận hành, lịch sử thay đổi và chi phí AI gắn với từng tác vụ.
          </p>
        </div>
        <StatusPill tone={runsQuery.isError ? "error" : "success"}>
          {runsQuery.isError ? "Mất kết nối" : "Đã kết nối"}
        </StatusPill>
      </section>

      <section className="mb-gutter grid grid-cols-1 gap-gutter md:grid-cols-4">
        <StatCard icon="smart_toy" label="Phiên chạy" value={(stats?.totalSessions ?? 0).toLocaleString("vi-VN")} />
        <StatCard icon="play_circle" label="Đang chạy" value={(stats?.runningSessions ?? 0).toLocaleString("vi-VN")} />
        <StatCard icon="receipt_long" label="Sự kiện vận hành" value={(stats?.traceEvents ?? 0).toLocaleString("vi-VN")} />
        <StatCard icon="toll" label="Lượng dùng 30 ngày" value={(stats?.tokensLast30Days ?? 0).toLocaleString("vi-VN")} />
      </section>

      <section className="grid grid-cols-1 gap-gutter xl:grid-cols-[minmax(0,1fr)_420px]">
        <Card className="overflow-hidden p-0">
          <div className="flex flex-col gap-3 border-b border-outline p-card-padding lg:flex-row lg:items-center lg:justify-between">
            <div>
              <h2 className="text-headline-sm font-bold text-secondary">Lượt chạy tác vụ</h2>
              <p className="mt-1 text-body-md text-on-surface-variant">Danh sách phiên chạy mới nhất của hệ thống agent.</p>
            </div>
            <div className="flex flex-col gap-2 sm:flex-row">
              <input
                className="w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary sm:w-64"
                onChange={(event) => setQuery(event.target.value)}
                placeholder="Tìm agent hoặc mục tiêu"
                type="search"
                value={query}
              />
              <select
                className="rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
                onChange={(event) => setStatus(event.target.value)}
                value={status}
              >
                <option value="">Tất cả trạng thái</option>
                <option value="running">Đang chạy</option>
                <option value="completed">Hoàn tất</option>
                <option value="failed">Thất bại</option>
                <option value="error">Lỗi</option>
              </select>
            </div>
          </div>

          {runsQuery.isLoading ? (
            <div className="p-card-padding text-body-md text-on-surface-variant">Đang tải nhật ký tác vụ...</div>
          ) : runsQuery.isError ? (
            <div className="m-card-padding rounded border border-error/30 bg-red-50 p-4 text-body-md text-error">
              Không thể tải nhật ký tác vụ. Vui lòng thử lại hoặc kiểm tra quyền truy cập.
            </div>
          ) : (
            <>
              <RunTable activeRunId={activeRunId} onSelect={setSelectedRunId} runs={runs} />
              <InfiniteScrollSentinel
                hasNextPage={runsList.hasNextPage}
                isFetchingNextPage={runsList.isFetchingNextPage}
                onLoadMore={runsList.fetchNextPage}
              />
            </>
          )}
        </Card>

        <aside className="space-y-gutter">
          <Card>
            <div className="mb-4 flex items-start justify-between gap-3">
              <div>
                <p className="text-label-caps uppercase text-on-surface-variant">Chi tiết lượt chạy</p>
                <h2 className="mt-1 text-headline-sm font-bold text-secondary">
                  {detailQuery.data ? shortId(detailQuery.data.run.id) : activeRunId ? shortId(activeRunId) : "Chưa chọn"}
                </h2>
              </div>
              {detailQuery.data ? (
                <StatusPill tone={statusTone(detailQuery.data.run.status)}>{statusLabel(detailQuery.data.run.status)}</StatusPill>
              ) : null}
            </div>
            {detailQuery.isLoading ? (
              <p className="text-body-md text-on-surface-variant">Đang tải sự kiện vận hành...</p>
            ) : detailQuery.isError ? (
              <p className="text-body-md text-error">Không thể tải chi tiết lượt chạy.</p>
            ) : (
              <TraceList traces={traces} />
            )}
          </Card>

          <Card>
            <div className="mb-4">
              <p className="text-label-caps uppercase text-on-surface-variant">Sự kiện quản trị</p>
              <h2 className="mt-1 text-headline-sm font-bold text-secondary">{runAudit.length ? "Theo lượt chạy đang chọn" : "Gần đây"}</h2>
            </div>
            {auditQuery.isLoading && !runAudit.length ? (
              <p className="text-body-md text-on-surface-variant">Đang tải nhật ký quản trị...</p>
            ) : (
              <AuditList events={detailEvents} />
            )}
          </Card>
        </aside>
      </section>
    </AppShell>
  );
}
