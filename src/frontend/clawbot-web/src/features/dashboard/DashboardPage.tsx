import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { AppShell } from "@/shared/layout/AppShell";
import { Button, Card, MetricCard, StatusPill, type MetricCardProps } from "@/shared/ui";
import {
  getAgentPerformance,
  getAnomalies,
  getForecast,
  getFunnel,
  getOmnichannel,
  getOmnichannelDelta,
  type AgentPerformance,
  type AnomalyPoint,
  type ForecastPoint,
  type FunnelResponse,
  type MetricDelta,
  type OmniChannelRow,
} from "@/shared/api/analytics";
import { useNotificationsRealtime } from "@/features/notifications/useNotificationsRealtime";

type RangePreset = "7d" | "30d";
type StatusTone = "success" | "warning" | "error" | "neutral";

interface DateRange {
  readonly from: string;
  readonly to: string;
}

const EMPTY_ROWS: readonly OmniChannelRow[] = [];
const EMPTY_DELTAS: readonly MetricDelta[] = [];
const EMPTY_AGENTS: readonly AgentPerformance[] = [];
const EMPTY_ANOMALIES: readonly AnomalyPoint[] = [];
const EMPTY_FORECAST: readonly ForecastPoint[] = [];

function isoDate(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function buildRange(preset: RangePreset): DateRange {
  const to = new Date();
  const from = new Date(to);
  from.setDate(to.getDate() - (preset === "30d" ? 29 : 6));
  return { from: isoDate(from), to: isoDate(to) };
}

function formatNumber(value: number): string {
  return value.toLocaleString("vi-VN");
}

function formatCurrency(value: number | null): string {
  if (value == null) return "—";
  return new Intl.NumberFormat("vi-VN", {
    style: "currency",
    currency: "VND",
    maximumFractionDigits: 0,
  }).format(value);
}

function formatPct(value: number | null | undefined): string {
  if (value == null) return "—";
  return `${value > 0 ? "+" : ""}${value.toFixed(1)}%`;
}

function percent(value: number): string {
  return `${Math.round(value * 100)}%`;
}

function platformLabel(platform: string | null | undefined): string {
  const normalized = (platform ?? "").toLowerCase();
  if (normalized === "facebook") return "Facebook";
  if (normalized === "zalo") return "Zalo";
  if (normalized === "instagram") return "Instagram";
  if (normalized === "tiktok") return "TikTok";
  if (normalized === "youtube") return "YouTube";
  if (normalized === "all") return "Tất cả kênh";
  return platform || "Khác";
}

function metricLabel(metric: string): string {
  if (metric === "leads") return "Lead";
  if (metric === "dms") return "Hội thoại";
  if (metric === "replies") return "Phản hồi";
  if (metric === "conversions") return "Chuyển đổi";
  if (metric === "adSpend") return "Chi phí quảng cáo";
  if (metric === "avgResponseTimeSec") return "Phản hồi trung bình";
  if (metric === "cpl") return "Chi phí/lead";
  return metric;
}

function metricTone(delta: number | null | undefined, lowerIsBetter = false): StatusTone {
  if (delta == null) return "neutral";
  if (lowerIsBetter) return delta <= 0 ? "success" : "warning";
  return delta >= 0 ? "success" : "warning";
}

function realtimeLabel(state: ReturnType<typeof useNotificationsRealtime>): string {
  if (state === "connected") return "Cập nhật tức thì";
  if (state === "connecting" || state === "reconnecting") return "Đang nối lại";
  if (state === "disabled") return "Cập nhật tức thì tắt";
  return "Cập nhật tức thì gián đoạn";
}

function realtimeTone(state: ReturnType<typeof useNotificationsRealtime>): StatusTone {
  if (state === "connected") return "success";
  if (state === "connecting" || state === "reconnecting") return "warning";
  return "neutral";
}

function aggregate(rows: readonly OmniChannelRow[]) {
  const sum = (select: (row: OmniChannelRow) => number) => rows.reduce((total, row) => total + select(row), 0);
  const avgResponses = rows.map((row) => row.avgResponseTimeSec).filter((value): value is number => value != null);
  const adSpend = rows.reduce((total, row) => total + (row.adSpend ?? 0), 0);
  const leads = sum((row) => row.leads);
  return {
    leads,
    dms: sum((row) => row.dms),
    replies: sum((row) => row.replies),
    conversions: sum((row) => row.conversions),
    avgResponse: avgResponses.length ? avgResponses.reduce((total, value) => total + value, 0) / avgResponses.length : null,
    adSpend,
    cpl: leads > 0 && adSpend > 0 ? adSpend / leads : null,
  };
}

function findDelta(metrics: readonly MetricDelta[], key: string): MetricDelta | undefined {
  return metrics.find((item) => item.metric === key);
}

function metricCards(rows: readonly OmniChannelRow[], deltas: readonly MetricDelta[]): readonly MetricCardProps[] {
  const agg = aggregate(rows);
  const dmsDelta = findDelta(deltas, "dms")?.deltaPct;
  const leadsDelta = findDelta(deltas, "leads")?.deltaPct;
  const conversionDelta = findDelta(deltas, "conversions")?.deltaPct;
  const responseDelta = findDelta(deltas, "avgResponseTimeSec")?.deltaPct;
  return [
    {
      label: "Hội thoại AI xử lý",
      value: formatNumber(agg.dms),
      delta: `So với tuần trước ${formatPct(dmsDelta)}`,
      icon: "forum",
      tone: metricTone(dmsDelta),
    },
    {
      label: "Lead mới",
      value: formatNumber(agg.leads),
      delta: `So với tuần trước ${formatPct(leadsDelta)}`,
      icon: "local_fire_department",
      tone: metricTone(leadsDelta),
    },
    {
      label: "Chuyển đổi",
      value: formatNumber(agg.conversions),
      delta: `So với tuần trước ${formatPct(conversionDelta)}`,
      icon: "trending_up",
      tone: metricTone(conversionDelta),
    },
    {
      label: "Phản hồi trung bình",
      value: agg.avgResponse != null ? `${agg.avgResponse.toFixed(1)} giây` : "—",
      delta: `Mục tiêu dưới 3 giây · ${formatPct(responseDelta)}`,
      icon: "bolt",
      tone: metricTone(responseDelta, true),
    },
  ];
}

function maxRowValue(rows: readonly OmniChannelRow[]): number {
  return Math.max(1, ...rows.flatMap((row) => [row.leads, row.dms, row.replies, row.conversions]));
}

function ChannelChart({ rows }: { readonly rows: readonly OmniChannelRow[] }) {
  const max = maxRowValue(rows);
  if (rows.length === 0) {
    return (
      <div className="flex min-h-[280px] items-center justify-center rounded-lg border border-dashed border-outline bg-surface text-body-md text-on-surface-variant">
        Chưa có dữ liệu kênh trong khoảng thời gian này.
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {rows.map((row) => (
        <div key={row.platform} className="grid grid-cols-[92px_minmax(0,1fr)] items-center gap-3">
          <div>
            <p className="truncate text-body-md font-semibold text-on-surface">{platformLabel(row.platform)}</p>
            <p className="font-mono text-mono-status text-on-surface-variant">{formatCurrency(row.cpl)} / lead</p>
          </div>
          <div className="space-y-1.5">
            {[
              { label: "Tin nhắn", value: row.dms, className: "bg-primary" },
              { label: "Lead", value: row.leads, className: "bg-tertiary" },
              { label: "Phản hồi", value: row.replies, className: "bg-warning" },
              { label: "Chuyển đổi", value: row.conversions, className: "bg-secondary" },
            ].map((item) => (
              <div key={item.label} className="grid grid-cols-[44px_minmax(0,1fr)_44px] items-center gap-2">
                <span className="font-mono text-mono-status text-on-surface-variant">{item.label}</span>
                <div className="h-2 rounded-full bg-surface-container">
                  <div className={`h-2 rounded-full ${item.className}`} style={{ width: `${Math.max(4, (item.value / max) * 100)}%` }} />
                </div>
                <span className="text-right font-mono text-mono-status text-secondary">{formatNumber(item.value)}</span>
              </div>
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}

function ForecastChart({ points }: { readonly points: readonly ForecastPoint[] }) {
  if (points.length === 0) {
    return (
      <div className="flex min-h-[180px] items-center justify-center rounded-lg border border-dashed border-outline bg-surface text-body-md text-on-surface-variant">
        Chưa có dự báo mới trong 24 giờ.
      </div>
    );
  }

  const width = 540;
  const height = 160;
  const padding = 22;
  const values = points.flatMap((point) => [point.lowerBound, point.value, point.upperBound]);
  const min = Math.min(...values);
  const max = Math.max(...values);
  const spread = Math.max(1, max - min);
  const x = (index: number) => padding + (index / Math.max(1, points.length - 1)) * (width - padding * 2);
  const y = (value: number) => height - padding - ((value - min) / spread) * (height - padding * 2);
  const mainPath = points.map((point, index) => `${x(index)},${y(point.value)}`).join(" ");
  const upperPath = points.map((point, index) => `${x(index)},${y(point.upperBound)}`).join(" ");
  const lowerPath = points.map((point, index) => `${x(index)},${y(point.lowerBound)}`).join(" ");
  const areaPath = `${points.map((point, index) => `${x(index)},${y(point.upperBound)}`).join(" ")} ${[...points]
    .reverse()
    .map((point, index) => `${x(points.length - 1 - index)},${y(point.lowerBound)}`)
    .join(" ")}`;

  return (
    <div>
      <svg viewBox={`0 0 ${width} ${height}`} className="h-[180px] w-full" role="img" aria-label="Dự báo lead 7 ngày">
        <polygon points={areaPath} className="fill-primary/10" />
        <polyline points={upperPath} fill="none" className="stroke-primary/25" strokeWidth="2" strokeDasharray="4 6" />
        <polyline points={lowerPath} fill="none" className="stroke-primary/25" strokeWidth="2" strokeDasharray="4 6" />
        <polyline points={mainPath} fill="none" className="stroke-primary" strokeWidth="4" strokeLinecap="round" strokeLinejoin="round" />
        {points.map((point, index) => (
          <circle key={point.date} cx={x(index)} cy={y(point.value)} r="4" className="fill-primary stroke-white" strokeWidth="2" />
        ))}
      </svg>
      <div className="flex justify-between font-mono text-mono-status text-on-surface-variant">
        <span>{points[0]?.date}</span>
        <span>{points.at(-1)?.date}</span>
      </div>
    </div>
  );
}

function FunnelPanel({ funnel }: { readonly funnel: FunnelResponse | null }) {
  const steps = funnel
    ? [
        { label: "Lead", value: funnel.leads, rate: 1 },
        { label: "Tin nhắn", value: funnel.dms, rate: funnel.dmRate },
        { label: "Phản hồi", value: funnel.replies, rate: funnel.replyRate },
        { label: "Chuyển đổi", value: funnel.conversions, rate: funnel.conversionRate },
      ]
    : [];

  return (
    <Card>
      <div className="mb-4 flex items-center justify-between">
        <h2 className="text-headline-sm">Phễu chuyển đổi</h2>
        <StatusPill tone="neutral">{funnel ? platformLabel(funnel.platform) : "Chưa có dữ liệu"}</StatusPill>
      </div>
      {steps.length === 0 ? (
        <p className="text-body-md text-on-surface-variant">Không tải được dữ liệu phễu chuyển đổi.</p>
      ) : (
        <div className="space-y-3">
          {steps.map((step) => (
            <div key={step.label}>
              <div className="mb-1 flex items-center justify-between">
                <span className="text-body-md font-semibold">{step.label}</span>
                <span className="font-mono text-mono-status text-secondary">
                  {formatNumber(step.value)} · {percent(step.rate)}
                </span>
              </div>
              <div className="h-2 rounded-full bg-surface-container">
                <div className="h-2 rounded-full bg-primary" style={{ width: `${Math.max(5, Math.min(100, step.rate * 100))}%` }} />
              </div>
            </div>
          ))}
        </div>
      )}
    </Card>
  );
}

function AgentStatus({ agents }: { readonly agents: readonly AgentPerformance[] }) {
  const visible = agents.slice(0, 4);
  return (
    <Card>
      <div className="mb-4 flex items-center justify-between">
        <h2 className="text-headline-sm">Trạng thái Agent</h2>
        <StatusPill tone={visible.length ? "success" : "neutral"}>{visible.length ? `${visible.length} đang hoạt động` : "Chưa có phiên chạy"}</StatusPill>
      </div>
      {visible.length === 0 ? (
        <p className="text-body-md text-on-surface-variant">Chưa có dữ liệu hiệu suất agent.</p>
      ) : (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          {visible.map((agent) => {
            const healthy = agent.completionRate >= 0.8;
            return (
              <div key={agent.agentId ?? agent.agentName} className="rounded-lg border border-outline bg-surface p-3">
                <div className="flex items-center justify-between gap-2">
                  <div className="min-w-0">
                    <p className="truncate text-body-md font-bold text-on-surface">{agent.agentName}</p>
                    <p className="font-mono text-mono-status text-on-surface-variant">{formatNumber(agent.traceCount)} sự kiện</p>
                  </div>
                  <StatusPill tone={healthy ? "success" : "warning"}>{healthy ? "Đang hoạt động" : "Cần kiểm tra"}</StatusPill>
                </div>
                <div className="mt-3 flex items-center justify-between font-mono text-mono-status text-secondary">
                  <span>{formatNumber(agent.sessions)} lượt xử lý</span>
                  <span>{percent(agent.completionRate)} hoàn tất</span>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </Card>
  );
}

function QuickActions() {
  const actions = [
    { to: "/conversations", icon: "add_comment", label: "Mở hộp thư ưu tiên", detail: "Duyệt hội thoại nóng" },
    { to: "/kb", icon: "upload_file", label: "Cập nhật tri thức", detail: "Phát hành phiên bản mới" },
    { to: "/notifications", icon: "notifications_active", label: "Xem cảnh báo", detail: "Lead nóng và bất thường" },
  ];

  return (
    <Card>
      <h2 className="mb-4 text-headline-sm">Hành động nhanh</h2>
      <div className="space-y-2">
        {actions.map((action) => (
          <Link
            key={action.to}
            to={action.to}
            className="flex items-center justify-between rounded-lg border border-outline bg-surface p-3 text-body-md font-semibold text-on-surface transition-colors hover:border-primary hover:text-primary"
          >
            <span className="flex min-w-0 items-center gap-2">
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">{action.icon}</span>
              <span className="min-w-0">
                <span className="block truncate">{action.label}</span>
                <span className="block text-label-sm font-normal text-on-surface-variant">{action.detail}</span>
              </span>
            </span>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">chevron_right</span>
          </Link>
        ))}
      </div>
    </Card>
  );
}

function LiveTaskTable({ anomalies }: { readonly anomalies: readonly AnomalyPoint[] }) {
  const rows = anomalies.slice(-5).reverse();
  return (
    <Card className="mt-stack-lg">
      <div className="mb-4 flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h2 className="text-headline-sm">Nhật ký tác vụ trực tiếp</h2>
          <p className="text-body-md text-on-surface-variant">Dòng cảnh báo mới từ agent báo cáo và trung tâm thông báo.</p>
        </div>
        <StatusPill tone={rows.some((row) => row.isAnomaly) ? "warning" : "success"}>
          {rows.some((row) => row.isAnomaly) ? "Có bất thường" : "Ổn định"}
        </StatusPill>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[680px] border-collapse">
          <thead>
            <tr className="border-b border-outline bg-surface text-left text-label-caps uppercase text-on-surface-variant">
              <th className="px-3 py-2">Mã tác vụ</th>
              <th className="px-3 py-2">Agent xử lý</th>
              <th className="px-3 py-2">Phân loại</th>
              <th className="px-3 py-2">Thời lượng</th>
              <th className="px-3 py-2 text-right">Trạng thái</th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr>
                <td className="px-3 py-8 text-center text-body-md text-on-surface-variant" colSpan={5}>
                  Chưa có cảnh báo bất thường.
                </td>
              </tr>
            ) : (
              rows.map((row, index) => (
                <tr key={`${row.date}-${row.platform}-${index}`} className="border-b border-outline text-body-md">
                  <td className="px-3 py-3 font-mono text-mono-status text-secondary">TK-{String(index + 9211).padStart(4, "0")}</td>
                  <td className="px-3 py-3">Báo cáo agent</td>
                  <td className="px-3 py-3">{metricLabel(row.metric)} · {platformLabel(row.platform)}</td>
                  <td className="px-3 py-3 font-mono text-mono-status">Mức lệch {row.zScore.toFixed(2)}</td>
                  <td className="px-3 py-3 text-right">
                    <StatusPill tone={row.isAnomaly ? "warning" : "success"}>{row.isAnomaly ? "Cần xử lý" : "Hoàn thành"}</StatusPill>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </Card>
  );
}

export default function DashboardPage() {
  const [rangePreset, setRangePreset] = useState<RangePreset>("7d");
  const range = useMemo(() => buildRange(rangePreset), [rangePreset]);
  const realtimeState = useNotificationsRealtime(true);

  const omnichannelQuery = useQuery({
    queryKey: ["analytics", "omnichannel", range],
    queryFn: () => getOmnichannel(range),
    refetchInterval: 30_000,
  });
  const deltaQuery = useQuery({
    queryKey: ["analytics", "omnichannel-delta", range, "wow"],
    queryFn: () => getOmnichannelDelta({ ...range, compare: "wow" }),
    refetchInterval: 60_000,
  });
  const funnelQuery = useQuery({
    queryKey: ["analytics", "funnel", range],
    queryFn: () => getFunnel(range),
    refetchInterval: 60_000,
  });
  const agentsQuery = useQuery({
    queryKey: ["analytics", "agent-performance", range],
    queryFn: () => getAgentPerformance(range),
    refetchInterval: 60_000,
  });
  const forecastQuery = useQuery({
    queryKey: ["analytics", "forecast", "leads"],
    queryFn: () => getForecast({ metric: "leads", platform: "all", horizon: 7 }),
    refetchInterval: 120_000,
  });
  const anomaliesQuery = useQuery({
    queryKey: ["analytics", "anomalies", "cpl"],
    queryFn: () => getAnomalies({ metric: "cpl", platform: "all", zThreshold: 3, lookbackDays: 14 }),
    refetchInterval: 60_000,
  });

  const rows = Array.isArray(omnichannelQuery.data?.rows) ? omnichannelQuery.data.rows : EMPTY_ROWS;
  const deltas = Array.isArray(deltaQuery.data?.metrics) ? deltaQuery.data.metrics : EMPTY_DELTAS;
  const agents = Array.isArray(agentsQuery.data) ? agentsQuery.data : EMPTY_AGENTS;
  const anomalies = Array.isArray(anomaliesQuery.data) ? anomaliesQuery.data : EMPTY_ANOMALIES;
  const forecast = Array.isArray(forecastQuery.data) ? forecastQuery.data : EMPTY_FORECAST;
  const cards = metricCards(rows, deltas);
  const agg = aggregate(rows);
  const apiError = omnichannelQuery.isError || deltaQuery.isError || funnelQuery.isError;

  return (
    <AppShell title="Tổng quan">
      <section className="mb-gutter rounded-lg border border-primary/20 bg-primary/5 p-4">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
          <div>
            <h1 className="text-headline-md">Tổng quan vận hành</h1>
            <p className="mt-1 text-body-md text-on-surface-variant">
              Giám sát hiệu năng Agent và hiệu quả tiếp cận theo thời gian thực.
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <StatusPill tone={realtimeTone(realtimeState)}>{realtimeLabel(realtimeState)}</StatusPill>
            <StatusPill tone={apiError ? "error" : omnichannelQuery.data?.stale ? "warning" : "success"}>
              {apiError ? "Mất kết nối" : omnichannelQuery.data?.stale ? "Dữ liệu cũ" : "Báo cáo sẵn sàng"}
            </StatusPill>
            <div className="flex rounded border border-outline bg-white p-1">
              {(["7d", "30d"] as const).map((item) => (
                <button
                  key={item}
                  type="button"
                  onClick={() => setRangePreset(item)}
                  className={[
                    "rounded px-3 py-1.5 font-mono text-mono-status transition-colors",
                    rangePreset === item ? "bg-primary text-on-primary" : "text-on-surface-variant hover:bg-surface-container-low",
                  ].join(" ")}
                >
                  {item === "7d" ? "7 ngày" : "30 ngày"}
                </button>
              ))}
            </div>
          </div>
        </div>
      </section>

      <section className="grid grid-cols-1 gap-gutter sm:grid-cols-2 xl:grid-cols-4">
        {cards.map((metric) => (
          <MetricCard key={metric.label} {...metric} />
        ))}
      </section>

      <section className="mt-stack-lg grid grid-cols-1 gap-gutter xl:grid-cols-[minmax(0,2fr)_minmax(320px,1fr)]">
        <Card>
          <div className="mb-4 flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <h2 className="text-headline-sm">Xu hướng đa kênh</h2>
              <p className="text-body-md text-on-surface-variant">
                {omnichannelQuery.isLoading
                  ? "Đang tải dữ liệu đa kênh..."
                  : `Kỳ ${range.from} → ${range.to}, tổng chi phí ${formatCurrency(agg.adSpend)}.`}
              </p>
            </div>
            <StatusPill tone="neutral">{rows.length} kênh</StatusPill>
          </div>
          <ChannelChart rows={rows} />
        </Card>

        <div className="space-y-gutter">
          <QuickActions />
          <FunnelPanel funnel={funnelQuery.data ?? null} />
        </div>
      </section>

      <section className="mt-stack-lg grid grid-cols-1 gap-gutter xl:grid-cols-[minmax(0,1.4fr)_minmax(320px,1fr)]">
        <Card>
          <div className="mb-4 flex items-center justify-between">
            <div>
              <h2 className="text-headline-sm">Dự báo lead 7 ngày</h2>
              <p className="text-body-md text-on-surface-variant">Nguồn: dữ liệu dự báo lead.</p>
            </div>
            <StatusPill tone={forecast.length ? "success" : "neutral"}>{forecast.length ? "Mới cập nhật" : "Chưa có dự báo"}</StatusPill>
          </div>
          <ForecastChart points={forecast} />
        </Card>
        <AgentStatus agents={agents} />
      </section>

      <LiveTaskTable anomalies={anomalies} />

      {apiError ? (
        <Card className="mt-stack-lg border-error/30 bg-error/5">
          <div className="flex items-start gap-3">
            <span aria-hidden="true" className="material-symbols-outlined text-error">error</span>
            <div>
              <h2 className="text-headline-sm text-error">Không tải đủ dữ liệu tổng quan</h2>
              <p className="mt-1 text-body-md text-on-surface-variant">
                Kiểm tra phiên đăng nhập và quyền truy cập. Các khối thông tin độc lập vẫn giữ trạng thái riêng để không làm gián đoạn trang.
              </p>
              <Button type="button" className="mt-3" variant="outline" onClick={() => void omnichannelQuery.refetch()}>
                <span aria-hidden="true" className="material-symbols-outlined text-[18px]">refresh</span>
                Tải lại báo cáo
              </Button>
            </div>
          </div>
        </Card>
      ) : null}
    </AppShell>
  );
}
