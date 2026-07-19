import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import {
  getSystemLog,
  listRequestStatsHourly,
  type RequestStatsPoint,
  type SystemLogEntry,
  type SystemLogSummary,
} from "@/shared/api/admin";
import { Button, Card, StatusPill, type StatusTone } from "@/shared/ui";
import { formatDateTime } from "./adminHelpers";
import { EmptyState, MetricTile } from "./adminUi";

interface AdminSystemLogsTabProps {
  readonly logs: readonly SystemLogEntry[];
  readonly summary: SystemLogSummary | null;
  readonly level: string;
  readonly statusGroup: string;
  readonly source: string;
  readonly from: string;
  readonly to: string;
  readonly q: string;
  readonly onLevelChange: (value: string) => void;
  readonly onStatusGroupChange: (value: string) => void;
  readonly onSourceChange: (value: string) => void;
  readonly onFromChange: (value: string) => void;
  readonly onToChange: (value: string) => void;
  readonly onSearchChange: (value: string) => void;
  readonly isLoading: boolean;
  readonly canLoadStats: boolean;
}

function levelTone(level: string): StatusTone {
  if (level === "Error" || level === "Fatal") return "error";
  if (level === "Warning") return "warning";
  return "neutral";
}

function levelLabel(level: string): string {
  if (level === "Error" || level === "Fatal") return "Lỗi";
  if (level === "Warning") return "Cảnh báo";
  return level;
}

function pathOrCategory(log: SystemLogEntry): string {
  if (log.method && log.path) return `${log.method} ${log.path}`;
  if (log.path) return log.path;
  return log.category ?? log.source;
}

function RequestVolumeChart({ points }: { readonly points: readonly RequestStatsPoint[] }) {
  if (!points.length) {
    return (
      <div className="flex min-h-[140px] items-center justify-center rounded-md border border-dashed border-outline bg-surface text-body-md text-on-surface-variant">
        Chưa có thống kê request (2xx/4xx/5xx) trong 24 giờ. Số liệu cập nhật mỗi phút.
      </div>
    );
  }

  const max = Math.max(
    1,
    ...points.map((p) => Math.max(p.ok2xx, p.client4xx, p.server5xx)),
  );

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-4 text-label-sm text-on-surface-variant">
        <span className="inline-flex items-center gap-1">
          <span className="inline-block h-2 w-3 rounded-sm bg-primary" /> 2xx
        </span>
        <span className="inline-flex items-center gap-1">
          <span className="inline-block h-2 w-3 rounded-sm bg-warning" /> 4xx
        </span>
        <span className="inline-flex items-center gap-1">
          <span className="inline-block h-2 w-3 rounded-sm bg-error" /> 5xx
        </span>
      </div>
      <div className="flex h-36 items-end gap-1 overflow-x-auto">
        {points.map((p) => {
          const hour = new Date(p.bucketHour).getHours().toString().padStart(2, "0");
          return (
            <div key={p.bucketHour} className="flex min-w-[28px] flex-1 flex-col items-center gap-1">
              <div className="flex h-28 w-full items-end justify-center gap-0.5">
                <div
                  className="w-1.5 rounded-t-sm bg-primary"
                  style={{ height: `${Math.max(2, (p.ok2xx / max) * 100)}%` }}
                  title={`2xx: ${p.ok2xx}`}
                />
                <div
                  className="w-1.5 rounded-t-sm bg-warning"
                  style={{ height: `${Math.max(2, (p.client4xx / max) * 100)}%` }}
                  title={`4xx: ${p.client4xx}`}
                />
                <div
                  className="w-1.5 rounded-t-sm bg-error"
                  style={{ height: `${Math.max(2, (p.server5xx / max) * 100)}%` }}
                  title={`5xx: ${p.server5xx}`}
                />
              </div>
              <span className="font-mono text-[10px] text-on-surface-variant">{hour}h</span>
            </div>
          );
        })}
      </div>
    </div>
  );
}

export function AdminSystemLogsTab({
  logs,
  summary,
  level,
  statusGroup,
  source,
  from,
  to,
  q,
  onLevelChange,
  onStatusGroupChange,
  onSourceChange,
  onFromChange,
  onToChange,
  onSearchChange,
  isLoading,
  canLoadStats,
}: AdminSystemLogsTabProps) {
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const detailQuery = useQuery({
    queryKey: ["admin", "system-logs", selectedId],
    queryFn: () => getSystemLog(selectedId!),
    enabled: selectedId != null,
  });
  const statsQuery = useQuery({
    queryKey: ["admin", "system-logs", "stats", "hourly", 24],
    queryFn: () => listRequestStatsHourly({ hours: 24 }),
    enabled: canLoadStats,
    staleTime: 60_000,
  });

  const detail = detailQuery.data;
  const stats = statsQuery.data ?? [];

  return (
    <section className="space-y-gutter">
      <div className="grid grid-cols-1 gap-gutter sm:grid-cols-2 xl:grid-cols-3">
        <MetricTile
          icon="error"
          label="Lỗi 5xx / Exception (24h)"
          value={`${summary?.errors24h ?? 0}`}
          tone={(summary?.errors24h ?? 0) > 0 ? "error" : "success"}
        />
        <MetricTile
          icon="warning"
          label="Cảnh báo 4xx (24h)"
          value={`${summary?.warnings24h ?? 0}`}
          tone={(summary?.warnings24h ?? 0) > 0 ? "warning" : "neutral"}
        />
        <MetricTile
          icon="bug_report"
          label="Dòng đang hiển thị"
          value={`${logs.length}`}
          tone="neutral"
        />
      </div>

      <Card className="p-card-padding">
        <div className="mb-3">
          <h2 className="text-headline-sm text-secondary">Lưu lượng request 24h</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">
            2xx/4xx/5xx theo giờ (không ghi từng dòng 200). Cập nhật mỗi phút.
          </p>
        </div>
        {statsQuery.isLoading ? (
          <p className="text-body-md text-on-surface-variant">Đang tải thống kê...</p>
        ) : (
          <RequestVolumeChart points={stats} />
        )}
      </Card>

      <Card className="p-0">
        <div className="flex flex-col gap-3 border-b border-outline p-card-padding md:flex-row md:items-end md:justify-between">
          <div>
            <h2 className="text-headline-sm text-secondary">Lỗi hệ thống</h2>
            <p className="mt-1 text-body-md text-on-surface-variant">
              HTTP 4xx/5xx, exception, job fail — tra cứu bằng requestId.
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            <label className="flex flex-col gap-1 text-label-sm text-on-surface-variant">
              Mức
              <select
                className="min-w-[120px] rounded-md border border-outline bg-white px-3 py-2 text-body-md text-secondary"
                value={level}
                onChange={(e) => onLevelChange(e.target.value)}
              >
                <option value="">Tất cả</option>
                <option value="Error">Lỗi</option>
                <option value="Warning">Cảnh báo</option>
                <option value="Fatal">Fatal</option>
              </select>
            </label>
            <label className="flex flex-col gap-1 text-label-sm text-on-surface-variant">
              Mã HTTP
              <select
                className="min-w-[110px] rounded-md border border-outline bg-white px-3 py-2 text-body-md text-secondary"
                value={statusGroup}
                onChange={(e) => onStatusGroupChange(e.target.value)}
              >
                <option value="">Tất cả</option>
                <option value="4xx">4xx</option>
                <option value="5xx">5xx</option>
              </select>
            </label>
            <label className="flex flex-col gap-1 text-label-sm text-on-surface-variant">
              Nguồn
              <select
                className="min-w-[140px] rounded-md border border-outline bg-white px-3 py-2 text-body-md text-secondary"
                value={source}
                onChange={(e) => onSourceChange(e.target.value)}
              >
                <option value="">Tất cả</option>
                <option value="api">api</option>
                <option value="agent-service">agent-service</option>
              </select>
            </label>
            <label className="flex flex-col gap-1 text-label-sm text-on-surface-variant">
              Từ
              <input
                type="datetime-local"
                className="rounded-md border border-outline bg-white px-3 py-2 text-body-md text-secondary"
                value={from}
                onChange={(e) => onFromChange(e.target.value)}
              />
            </label>
            <label className="flex flex-col gap-1 text-label-sm text-on-surface-variant">
              Đến
              <input
                type="datetime-local"
                className="rounded-md border border-outline bg-white px-3 py-2 text-body-md text-secondary"
                value={to}
                onChange={(e) => onToChange(e.target.value)}
              />
            </label>
            <label className="flex min-w-[200px] flex-1 flex-col gap-1 text-label-sm text-on-surface-variant">
              Tìm (path / message / requestId)
              <input
                className="rounded-md border border-outline bg-white px-3 py-2 text-body-md text-secondary"
                value={q}
                onChange={(e) => onSearchChange(e.target.value)}
                placeholder="vd. internal_error hoặc /api/..."
              />
            </label>
          </div>
        </div>

        <div className="overflow-x-auto">
          <table className="min-w-[1100px] w-full border-collapse text-left">
            <thead className="bg-surface-variant text-label-sm uppercase text-secondary">
              <tr>
                <th className="px-4 py-3 font-bold">Thời điểm</th>
                <th className="px-4 py-3 font-bold">Mức</th>
                <th className="px-4 py-3 font-bold">Mã</th>
                <th className="px-4 py-3 font-bold">Nguồn</th>
                <th className="px-4 py-3 font-bold">Thông điệp</th>
                <th className="px-4 py-3 font-bold">RequestId</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-outline bg-white">
              {logs.map((log) => (
                <tr
                  key={log.id}
                  className="cursor-pointer hover:bg-surface-container-low"
                  onClick={() => setSelectedId(log.id)}
                >
                  <td className="whitespace-nowrap px-4 py-4 text-body-md text-on-surface-variant">
                    {formatDateTime(log.occurredAt)}
                  </td>
                  <td className="px-4 py-4">
                    <StatusPill tone={levelTone(log.level)}>{levelLabel(log.level)}</StatusPill>
                  </td>
                  <td className="px-4 py-4 font-mono text-mono-status text-secondary">
                    {log.statusCode ?? "—"}
                  </td>
                  <td className="max-w-[280px] truncate px-4 py-4 text-body-md text-secondary" title={pathOrCategory(log)}>
                    {pathOrCategory(log)}
                  </td>
                  <td className="max-w-[360px] truncate px-4 py-4 text-body-md text-on-surface-variant" title={log.message}>
                    {log.message}
                  </td>
                  <td className="max-w-[160px] truncate px-4 py-4 font-mono text-mono-status text-on-surface-variant">
                    {log.traceId ?? "—"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {!logs.length && !isLoading ? (
          <div className="p-card-padding">
            <EmptyState>Chưa có lỗi hệ thống phù hợp bộ lọc.</EmptyState>
          </div>
        ) : null}
      </Card>

      {selectedId != null ? (
        <div className="fixed inset-0 z-50 flex justify-end bg-black/30" role="dialog" aria-modal="true">
          <div className="flex h-full w-full max-w-xl flex-col bg-white shadow-xl">
            <div className="flex items-center justify-between border-b border-outline p-card-padding">
              <div>
                <h3 className="text-headline-sm text-secondary">Chi tiết lỗi #{selectedId}</h3>
                <p className="mt-1 text-body-md text-on-surface-variant">
                  {detail ? formatDateTime(detail.occurredAt) : "Đang tải..."}
                </p>
              </div>
              <Button type="button" variant="outline" onClick={() => setSelectedId(null)}>
                Đóng
              </Button>
            </div>
            <div className="flex-1 space-y-4 overflow-y-auto p-card-padding">
              {detailQuery.isError ? (
                <p className="text-body-md text-error">Không tải được chi tiết.</p>
              ) : null}
              {detail ? (
                <>
                  <div className="flex flex-wrap gap-2">
                    <StatusPill tone={levelTone(detail.level)}>{levelLabel(detail.level)}</StatusPill>
                    {detail.statusCode != null ? (
                      <StatusPill tone={detail.statusCode >= 500 ? "error" : "warning"}>{detail.statusCode}</StatusPill>
                    ) : null}
                    <StatusPill tone="neutral">{detail.source}</StatusPill>
                  </div>
                  <Field label="Thông điệp" value={detail.message} />
                  <Field
                    label="Request"
                    value={
                      detail.method || detail.path
                        ? `${detail.method ?? ""} ${detail.path ?? ""}`.trim()
                        : detail.category ?? "—"
                    }
                  />
                  <div className="flex items-center gap-2">
                    <Field label="RequestId / TraceId" value={detail.traceId ?? "—"} mono />
                    {detail.traceId ? (
                      <Button
                        type="button"
                        variant="outline"
                        onClick={() => void navigator.clipboard?.writeText(detail.traceId!)}
                      >
                        Sao chép
                      </Button>
                    ) : null}
                  </div>
                  {detail.elapsedMs != null ? <Field label="Thời gian" value={`${Math.round(detail.elapsedMs)} ms`} /> : null}
                  {detail.exception ? (
                    <div>
                      <p className="mb-1 text-label-sm uppercase text-on-surface-variant">Stack trace</p>
                      <pre className="max-h-[360px] overflow-auto rounded-md bg-surface-container-low p-3 text-mono-status text-secondary">
                        {detail.exception}
                      </pre>
                    </div>
                  ) : null}
                  {detail.properties ? (
                    <div>
                      <p className="mb-1 text-label-sm uppercase text-on-surface-variant">Properties</p>
                      <pre className="max-h-[200px] overflow-auto rounded-md bg-surface-container-low p-3 text-mono-status text-secondary">
                        {detail.properties}
                      </pre>
                    </div>
                  ) : null}
                </>
              ) : null}
            </div>
          </div>
        </div>
      ) : null}
    </section>
  );
}

function Field({ label, value, mono }: { readonly label: string; readonly value: string; readonly mono?: boolean }) {
  return (
    <div>
      <p className="mb-1 text-label-sm uppercase text-on-surface-variant">{label}</p>
      <p className={mono ? "break-all font-mono text-mono-status text-secondary" : "text-body-md text-secondary"}>{value}</p>
    </div>
  );
}
