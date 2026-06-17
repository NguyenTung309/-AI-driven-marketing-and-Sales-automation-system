import { useMemo, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { AxiosError } from "axios";
import { AppShell } from "@/shared/layout/AppShell";
import { Alert, Button, Card, Modal, StatusPill, type StatusTone } from "@/shared/ui";
import {
  downloadAnalyticsExport,
  getAgentCost,
  getAgentPerformance,
  getAnomalies,
  getForecast,
  getFunnel,
  getOmnichannel,
  getOmnichannelDelta,
  type AgentCostItem,
  type AgentPerformance,
  type AnomalyPoint,
  type ForecastPoint,
  type MetricDelta,
  type OmniChannelRow,
} from "@/shared/api/analytics";

type RangePreset = "7d" | "30d";
type ReportTab = "overview" | "agent" | "lead";
type ExportFormat = "csv" | "pdf";

interface DateRange {
  readonly from: string;
  readonly to: string;
}

interface AggregateMetrics {
  readonly leads: number;
  readonly dms: number;
  readonly replies: number;
  readonly conversions: number;
  readonly adSpend: number;
  readonly avgResponseTimeSec: number | null;
}

const EMPTY_ROWS: readonly OmniChannelRow[] = [];
const EMPTY_DELTAS: readonly MetricDelta[] = [];
const EMPTY_AGENTS: readonly AgentPerformance[] = [];
const EMPTY_COSTS: readonly AgentCostItem[] = [];
const EMPTY_ANOMALIES: readonly AnomalyPoint[] = [];
const EMPTY_FORECAST: readonly ForecastPoint[] = [];

const CHANNELS = ["facebook", "zalo", "tiktok", "website", "youtube"] as const;

function isoDate(value: Date): string {
  return value.toISOString().slice(0, 10);
}

function buildRange(preset: RangePreset): DateRange {
  const to = new Date();
  const from = new Date(to);
  from.setDate(to.getDate() - (preset === "30d" ? 29 : 6));
  return { from: isoDate(from), to: isoDate(to) };
}

function normalize(value: string | null | undefined): string {
  return (value ?? "").trim().toLowerCase();
}

function platformLabel(platform: string | null | undefined): string {
  const value = normalize(platform);
  if (value === "facebook") return "Facebook";
  if (value === "zalo") return "Zalo";
  if (value === "tiktok") return "TikTok";
  if (value === "website" || value === "web") return "Website";
  if (value === "youtube") return "YouTube";
  if (value === "all") return "Tất cả kênh";
  return platform || "Khác";
}

function platformIcon(platform: string): string {
  const value = normalize(platform);
  if (value === "facebook") return "thumb_up";
  if (value === "zalo") return "chat";
  if (value === "tiktok") return "music_note";
  if (value === "website" || value === "web") return "language";
  if (value === "youtube") return "play_circle";
  return "campaign";
}

function formatNumber(value: number): string {
  return value.toLocaleString("vi-VN");
}

function formatCurrency(value: number): string {
  return new Intl.NumberFormat("vi-VN", {
    style: "currency",
    currency: "VND",
    maximumFractionDigits: 0,
  }).format(value);
}

function formatUsd(value: number): string {
  return new Intl.NumberFormat("en-US", {
    style: "currency",
    currency: "USD",
    maximumFractionDigits: 2,
  }).format(value);
}

function formatPct(value: number | null | undefined): string {
  if (value == null || Number.isNaN(value)) return "—";
  return `${value.toFixed(1)}%`;
}

function rate(part: number, total: number): number {
  if (!total) return 0;
  return (part / total) * 100;
}

function aggregate(rows: readonly OmniChannelRow[]): AggregateMetrics {
  const responseTimes = rows
    .map((row) => row.avgResponseTimeSec)
    .filter((value): value is number => typeof value === "number");
  return {
    leads: rows.reduce((sum, row) => sum + row.leads, 0),
    dms: rows.reduce((sum, row) => sum + row.dms, 0),
    replies: rows.reduce((sum, row) => sum + row.replies, 0),
    conversions: rows.reduce((sum, row) => sum + row.conversions, 0),
    adSpend: rows.reduce((sum, row) => sum + (row.adSpend ?? 0), 0),
    avgResponseTimeSec: responseTimes.length
      ? responseTimes.reduce((sum, value) => sum + value, 0) / responseTimes.length
      : null,
  };
}

function deltaFor(deltas: readonly MetricDelta[], metric: string): MetricDelta | null {
  return deltas.find((item) => normalize(item.metric) === normalize(metric)) ?? null;
}

function deltaText(delta: MetricDelta | null): string {
  if (!delta || delta.deltaPct == null) return "Chưa có kỳ so sánh";
  const sign = delta.deltaPct > 0 ? "+" : "";
  return `${sign}${delta.deltaPct.toFixed(1)}% so với kỳ trước`;
}

function deltaTone(delta: MetricDelta | null, positiveIsGood = true): StatusTone {
  if (!delta || delta.deltaPct == null || delta.deltaPct === 0) return "neutral";
  const good = positiveIsGood ? delta.deltaPct > 0 : delta.deltaPct < 0;
  return good ? "success" : "warning";
}

function errorMessage(error: unknown): string {
  if (!error) return "";
  if (error instanceof AxiosError) {
    const data = error.response?.data as { error?: string; title?: string; detail?: string } | undefined;
    return data?.error ?? data?.title ?? data?.detail ?? error.message;
  }
  if (error instanceof Error) return error.message;
  return "Không tải được báo cáo. Vui lòng thử lại.";
}

function formatDate(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("vi-VN", { day: "2-digit", month: "2-digit" }).format(date);
}

function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(url);
}

function MetricCard({
  icon,
  label,
  value,
  meta,
  tone,
}: {
  readonly icon: string;
  readonly label: string;
  readonly value: string;
  readonly meta: string;
  readonly tone: StatusTone;
}) {
  return (
    <Card>
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-label-caps uppercase text-on-surface-variant">{label}</p>
          <p className="mt-2 text-telemetry-data text-secondary">{value}</p>
        </div>
        <span className="material-symbols-outlined rounded bg-primary/10 p-2 text-primary">{icon}</span>
      </div>
      <div className="mt-3">
        <StatusPill tone={tone}>{meta}</StatusPill>
      </div>
    </Card>
  );
}

function ChannelKpiGrid({ rows }: { readonly rows: readonly OmniChannelRow[] }) {
  const knownRows = CHANNELS.map((channel) => rows.find((row) => normalize(row.platform) === channel) ?? null);
  const maxLeads = Math.max(1, ...rows.map((row) => row.leads));

  return (
    <Card>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-headline-sm text-secondary">KPI 5 kênh</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">Leads, DM, phản hồi, chuyển đổi và CPL theo từng kênh.</p>
        </div>
        <StatusPill tone="neutral">{rows.length} dòng dữ liệu</StatusPill>
      </div>
      <div className="grid grid-cols-1 gap-3 lg:grid-cols-5">
        {knownRows.map((row, index) => {
          const channel = CHANNELS[index];
          const leads = row?.leads ?? 0;
          const width = `${Math.round((leads / maxLeads) * 100)}%`;
          return (
            <article key={channel} className="rounded-lg border border-outline bg-surface p-3">
              <div className="mb-3 flex items-center justify-between gap-2">
                <div className="flex items-center gap-2">
                  <span className="material-symbols-outlined text-[18px] text-primary">{platformIcon(channel)}</span>
                  <h3 className="text-body-md font-bold text-secondary">{platformLabel(channel)}</h3>
                </div>
                <StatusPill tone={row ? "success" : "neutral"}>{row ? "live" : "no data"}</StatusPill>
              </div>
              <p className="text-telemetry-data text-secondary">{formatNumber(leads)}</p>
              <p className="mt-1 text-label-sm text-on-surface-variant">lead</p>
              <div className="mt-3 h-2 rounded-full bg-surface-variant">
                <div className="h-full rounded-full bg-primary" style={{ width }} />
              </div>
              <dl className="mt-3 space-y-2 text-label-sm">
                <div className="flex justify-between gap-2">
                  <dt className="text-on-surface-variant">DM</dt>
                  <dd className="font-semibold text-secondary">{formatNumber(row?.dms ?? 0)}</dd>
                </div>
                <div className="flex justify-between gap-2">
                  <dt className="text-on-surface-variant">Reply</dt>
                  <dd className="font-semibold text-secondary">{formatPct(rate(row?.replies ?? 0, row?.dms ?? 0))}</dd>
                </div>
                <div className="flex justify-between gap-2">
                  <dt className="text-on-surface-variant">CPL</dt>
                  <dd className="font-semibold text-secondary">{row?.cpl == null ? "—" : formatCurrency(row.cpl)}</dd>
                </div>
              </dl>
            </article>
          );
        })}
      </div>
    </Card>
  );
}

function ChannelBars({ rows }: { readonly rows: readonly OmniChannelRow[] }) {
  const maxValue = Math.max(1, ...rows.flatMap((row) => [row.leads, row.dms, row.conversions]));
  return (
    <Card>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-headline-sm text-secondary">Xu hướng kênh</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">So sánh lead, hội thoại và chuyển đổi trong kỳ.</p>
        </div>
        <div className="flex gap-3 text-label-sm text-on-surface-variant">
          <span className="inline-flex items-center gap-1"><span className="size-2 rounded-full bg-primary" /> Lead</span>
          <span className="inline-flex items-center gap-1"><span className="size-2 rounded-full bg-warning" /> DM</span>
          <span className="inline-flex items-center gap-1"><span className="size-2 rounded-full bg-success" /> Chuyển đổi</span>
        </div>
      </div>
      <div className="space-y-4">
        {rows.length ? (
          rows.map((row) => (
            <div key={row.platform} className="grid grid-cols-[110px_minmax(0,1fr)] items-center gap-3">
              <div className="flex items-center gap-2">
                <span className="material-symbols-outlined text-[18px] text-primary">{platformIcon(row.platform)}</span>
                <span className="text-body-md font-semibold text-secondary">{platformLabel(row.platform)}</span>
              </div>
              <div className="space-y-1.5">
                <div className="h-2 rounded-full bg-surface-variant"><div className="h-full rounded-full bg-primary" style={{ width: `${(row.leads / maxValue) * 100}%` }} /></div>
                <div className="h-2 rounded-full bg-surface-variant"><div className="h-full rounded-full bg-warning" style={{ width: `${(row.dms / maxValue) * 100}%` }} /></div>
                <div className="h-2 rounded-full bg-surface-variant"><div className="h-full rounded-full bg-success" style={{ width: `${(row.conversions / maxValue) * 100}%` }} /></div>
              </div>
            </div>
          ))
        ) : (
          <div className="flex min-h-[260px] items-center justify-center rounded-lg border border-dashed border-outline bg-surface p-6 text-body-md text-on-surface-variant">
            Chưa có dữ liệu omnichannel cho kỳ này.
          </div>
        )}
      </div>
    </Card>
  );
}

function FunnelCard({ funnel }: { readonly funnel: { readonly platform?: string; readonly leads: number; readonly dms: number; readonly replies: number; readonly conversions: number; readonly dmRate: number; readonly replyRate: number; readonly conversionRate: number } | null }) {
  const steps = funnel
    ? [
        { label: "Lead", value: funnel.leads, rate: 100 },
        { label: "DM", value: funnel.dms, rate: funnel.dmRate },
        { label: "Reply", value: funnel.replies, rate: funnel.replyRate },
        { label: "Conversion", value: funnel.conversions, rate: funnel.conversionRate },
      ]
    : [];
  return (
    <Card>
      <h2 className="text-headline-sm text-secondary">Tỷ lệ cuộc gọi hỗ trợ</h2>
      <p className="mt-1 text-body-md text-on-surface-variant">Funnel chuyển đổi {platformLabel(funnel?.platform ?? "all")}.</p>
      <div className="mt-5 space-y-3">
        {steps.map((step) => (
          <div key={step.label}>
            <div className="mb-1 flex justify-between gap-3 text-label-sm">
              <span className="font-semibold text-secondary">{step.label}</span>
              <span className="text-on-surface-variant">{formatNumber(step.value)} · {formatPct(step.rate)}</span>
            </div>
            <div className="h-3 rounded-full bg-surface-variant">
              <div className="h-full rounded-full bg-success" style={{ width: `${Math.min(100, Math.max(0, step.rate))}%` }} />
            </div>
          </div>
        ))}
        {!steps.length ? <p className="text-body-md text-on-surface-variant">Chưa có funnel.</p> : null}
      </div>
    </Card>
  );
}

function ForecastCard({ points }: { readonly points: readonly ForecastPoint[] }) {
  const width = 420;
  const height = 160;
  const values = points.flatMap((point) => [point.lowerBound, point.value, point.upperBound]);
  const min = Math.min(...values, 0);
  const max = Math.max(...values, 1);
  const span = Math.max(1, max - min);
  const coords = points.map((point, index) => {
    const x = points.length <= 1 ? width / 2 : (index / (points.length - 1)) * width;
    const y = height - ((point.value - min) / span) * height;
    return `${x},${y}`;
  });
  return (
    <Card>
      <div className="mb-4 flex items-center justify-between gap-3">
        <div>
          <h2 className="text-headline-sm text-secondary">Dự báo lead 7 ngày</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">Metric `leads` từ `/forecast`.</p>
        </div>
        <StatusPill tone={points.length ? "success" : "neutral"}>{points.length ? "Fresh" : "No data"}</StatusPill>
      </div>
      {points.length ? (
        <div className="overflow-x-auto">
          <svg viewBox={`0 0 ${width} ${height + 34}`} className="min-w-[420px] w-full">
            <polyline points={coords.join(" ")} fill="none" stroke="#d32f2f" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" />
            {points.map((point, index) => {
              const [x, y] = coords[index].split(",").map(Number);
              return (
                <g key={`${point.date}-${index}`}>
                  <circle cx={x} cy={y} r="4" fill="#d32f2f" />
                  <text x={x} y={height + 24} textAnchor="middle" className="fill-on-surface-variant text-[10px]">
                    {formatDate(point.date)}
                  </text>
                </g>
              );
            })}
          </svg>
        </div>
      ) : (
        <div className="flex min-h-[160px] items-center justify-center rounded-lg border border-dashed border-outline bg-surface p-4 text-body-md text-on-surface-variant">
          Chưa đủ dữ liệu forecast.
        </div>
      )}
    </Card>
  );
}

function AgentRadar({ agents }: { readonly agents: readonly AgentPerformance[] }) {
  const topAgents = agents.slice(0, 5);
  const size = 220;
  const center = size / 2;
  const radius = 82;
  const points = topAgents.map((agent, index) => {
    const angle = (Math.PI * 2 * index) / Math.max(1, topAgents.length) - Math.PI / 2;
    const value = Math.max(0.08, Math.min(1, agent.completionRate));
    return {
      label: agent.agentName,
      x: center + Math.cos(angle) * radius * value,
      y: center + Math.sin(angle) * radius * value,
      tx: center + Math.cos(angle) * (radius + 24),
      ty: center + Math.sin(angle) * (radius + 24),
    };
  });
  const polygon = points.map((point) => `${point.x},${point.y}`).join(" ");
  return (
    <Card>
      <h2 className="text-headline-sm text-secondary">RAG Confidence Scores</h2>
      <p className="mt-1 text-body-md text-on-surface-variant">Radar hiệu suất theo completion rate.</p>
      <div className="mt-4 flex justify-center">
        <svg viewBox={`0 0 ${size} ${size}`} className="size-[260px] max-w-full">
          {[0.33, 0.66, 1].map((factor) => (
            <circle key={factor} cx={center} cy={center} r={radius * factor} fill="none" stroke="#e2e8f0" />
          ))}
          {polygon ? <polygon points={polygon} fill="rgba(211,47,47,0.16)" stroke="#d32f2f" strokeWidth="2" /> : null}
          {points.map((point) => (
            <g key={point.label}>
              <circle cx={point.x} cy={point.y} r="3" fill="#d32f2f" />
              <text x={point.tx} y={point.ty} textAnchor="middle" dominantBaseline="middle" className="fill-on-surface-variant text-[9px]">
                {point.label.slice(0, 12)}
              </text>
            </g>
          ))}
        </svg>
      </div>
    </Card>
  );
}

function AgentTable({ agents, costs }: { readonly agents: readonly AgentPerformance[]; readonly costs: readonly AgentCostItem[] }) {
  const costsByCode = new Map(costs.map((cost) => [normalize(cost.agentCode), cost]));
  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-outline p-card-padding">
        <div>
          <h2 className="text-headline-sm text-secondary">Bảng dữ liệu hiệu suất Agent</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">Sessions, chất lượng trả lời, trace và token cost.</p>
        </div>
        <StatusPill tone="neutral">{agents.length} agent</StatusPill>
      </div>
      <div className="overflow-x-auto">
        <table className="min-w-[900px] w-full border-collapse text-left">
          <thead className="bg-surface-variant text-label-sm uppercase text-secondary">
            <tr>
              <th className="px-4 py-3 font-bold">Tên Agent</th>
              <th className="px-4 py-3 font-bold">Tổng tác vụ</th>
              <th className="px-4 py-3 font-bold">Xử lý TB</th>
              <th className="px-4 py-3 font-bold">Chất lượng</th>
              <th className="px-4 py-3 font-bold">Token tiêu thụ</th>
              <th className="px-4 py-3 font-bold">Tỉ lệ lỗi</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-outline bg-white">
            {agents.map((agent) => {
              const cost = costsByCode.get(normalize(agent.agentName)) ?? costs.find((item) => normalize(agent.agentName).includes(normalize(item.agentCode)));
              const errorRate = Math.max(0, 100 - agent.completionRate * 100);
              return (
                <tr key={agent.agentId ?? agent.agentName} className="hover:bg-surface-container-low">
                  <td className="px-4 py-4 align-top">
                    <div className="flex items-center gap-2">
                      <span className="size-2 rounded-full bg-success" />
                      <span className="font-semibold text-secondary">{agent.agentName}</span>
                    </div>
                  </td>
                  <td className="px-4 py-4 align-top font-mono text-mono-status text-secondary">{formatNumber(agent.sessions)}</td>
                  <td className="px-4 py-4 align-top font-mono text-mono-status text-secondary">{formatPct(agent.completionRate * 100)}</td>
                  <td className="px-4 py-4 align-top">
                    <div className="font-mono text-mono-status text-secondary">
                      {agent.qualitySamples ? formatPct(agent.qualityPassRate * 100) : "—"}
                    </div>
                    <div className="mt-1 text-label-sm text-on-surface-variant">
                      {agent.qualitySamples
                        ? `${formatNumber(agent.passedQualitySamples)}/${formatNumber(agent.qualitySamples)} mẫu${
                            agent.averageQualityScore == null ? "" : ` · score ${agent.averageQualityScore.toFixed(2)}`
                          }`
                        : "Chưa có mẫu quality"}
                    </div>
                  </td>
                  <td className="px-4 py-4 align-top text-body-md text-secondary">
                    {cost ? `${formatNumber(cost.inputTokens + cost.outputTokens)} token · ${formatUsd(cost.usd)}` : `${formatNumber(agent.traceCount)} trace`}
                  </td>
                  <td className="px-4 py-4 align-top"><StatusPill tone={errorRate > 8 ? "error" : errorRate > 3 ? "warning" : "success"}>{formatPct(errorRate)}</StatusPill></td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </Card>
  );
}

function AnomalyList({ anomalies }: { readonly anomalies: readonly AnomalyPoint[] }) {
  return (
    <Card>
      <h2 className="text-headline-sm text-secondary">Anomaly & cảnh báo</h2>
      <p className="mt-1 text-body-md text-on-surface-variant">Điểm bất thường từ Report Agent.</p>
      <div className="mt-4 space-y-2">
        {anomalies.length ? (
          anomalies.slice(0, 6).map((item) => (
            <div key={`${item.date}-${item.platform}-${item.metric}`} className="rounded border border-outline bg-surface p-3">
              <div className="flex items-center justify-between gap-2">
                <p className="font-semibold text-secondary">{platformLabel(item.platform)} · {item.metric}</p>
                <StatusPill tone={item.isAnomaly ? "error" : "neutral"}>z {item.zScore.toFixed(1)}</StatusPill>
              </div>
              <p className="mt-1 text-label-sm text-on-surface-variant">{formatDate(item.date)} · value {formatNumber(item.value)}</p>
            </div>
          ))
        ) : (
          <div className="rounded-lg border border-dashed border-outline bg-surface p-4 text-body-md text-on-surface-variant">
            Không có anomaly trong kỳ hiện tại.
          </div>
        )}
      </div>
    </Card>
  );
}

function ExportDialog({
  open,
  exporting,
  error,
  onClose,
  onExport,
}: {
  readonly open: boolean;
  readonly exporting: boolean;
  readonly error: unknown;
  readonly onClose: () => void;
  readonly onExport: (format: ExportFormat) => void;
}) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Xuất báo cáo"
      footer={
        <>
          <Button type="button" variant="ghost" onClick={onClose} disabled={exporting}>Hủy</Button>
          <Button type="button" variant="outline" onClick={() => onExport("csv")} disabled={exporting}>
            <span className="material-symbols-outlined text-[18px]">table_view</span>
            CSV
          </Button>
          <Button type="button" onClick={() => onExport("pdf")} disabled={exporting}>
            <span className="material-symbols-outlined text-[18px]">picture_as_pdf</span>
            PDF
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        {error ? <Alert tone="error">{errorMessage(error)}</Alert> : null}
        <p className="text-body-md text-on-surface-variant">
          File export gọi `/api/analytics/export` bằng bearer token hiện tại. CSV dùng cho bảng số liệu, PDF dùng cho báo cáo gửi nội bộ.
        </p>
      </div>
    </Modal>
  );
}

export default function AnalyticsReportsPage() {
  const [rangePreset, setRangePreset] = useState<RangePreset>("7d");
  const [tab, setTab] = useState<ReportTab>("overview");
  const [platform, setPlatform] = useState("all");
  const [exportOpen, setExportOpen] = useState(false);
  const range = useMemo(() => buildRange(rangePreset), [rangePreset]);

  const omnichannelQuery = useQuery({
    queryKey: ["analytics-report", "omnichannel", range],
    queryFn: () => getOmnichannel(range),
    refetchInterval: 60_000,
  });
  const deltaQuery = useQuery({
    queryKey: ["analytics-report", "delta", range],
    queryFn: () => getOmnichannelDelta({ ...range, compare: "wow" }),
    refetchInterval: 120_000,
  });
  const funnelQuery = useQuery({
    queryKey: ["analytics-report", "funnel", range, platform],
    queryFn: () => getFunnel({ ...range, platform }),
  });
  const agentsQuery = useQuery({
    queryKey: ["analytics-report", "agents", range],
    queryFn: () => getAgentPerformance(range),
  });
  const costsQuery = useQuery({
    queryKey: ["analytics-report", "agent-cost", range],
    queryFn: () => getAgentCost(range),
  });
  const forecastQuery = useQuery({
    queryKey: ["analytics-report", "forecast", platform],
    queryFn: () => getForecast({ metric: "leads", platform, horizon: 7 }),
  });
  const anomaliesQuery = useQuery({
    queryKey: ["analytics-report", "anomalies", platform],
    queryFn: () => getAnomalies({ metric: "cpl", platform, lookbackDays: 14, zThreshold: 3 }),
  });
  const exportMutation = useMutation({
    mutationFn: (format: ExportFormat) => downloadAnalyticsExport({ ...range, format }),
    onSuccess: (blob, format) => {
      downloadBlob(blob, `analytics-${range.from}-${range.to}.${format}`);
      setExportOpen(false);
    },
  });

  const rows = Array.isArray(omnichannelQuery.data?.rows) ? omnichannelQuery.data.rows : EMPTY_ROWS;
  const deltas = Array.isArray(deltaQuery.data?.metrics) ? deltaQuery.data.metrics : EMPTY_DELTAS;
  const agents = Array.isArray(agentsQuery.data) ? agentsQuery.data : EMPTY_AGENTS;
  const costs = Array.isArray(costsQuery.data?.items) ? costsQuery.data.items : EMPTY_COSTS;
  const anomalies = Array.isArray(anomaliesQuery.data) ? anomaliesQuery.data : EMPTY_ANOMALIES;
  const forecast = Array.isArray(forecastQuery.data) ? forecastQuery.data : EMPTY_FORECAST;
  const agg = aggregate(rows);
  const replyRate = rate(agg.replies, agg.dms);
  const conversionRate = rate(agg.conversions, agg.leads);
  const apiError = omnichannelQuery.error ?? deltaQuery.error ?? funnelQuery.error ?? agentsQuery.error ?? costsQuery.error;
  const qualitySamples = agents.reduce((sum, agent) => sum + agent.qualitySamples, 0);
  const averageQualityPassRate = qualitySamples
    ? agents.reduce((sum, agent) => sum + agent.qualityPassRate * agent.qualitySamples, 0) / qualitySamples
    : null;

  const metrics = [
    {
      icon: "forum",
      label: "Tổng số hội thoại",
      value: formatNumber(agg.dms),
      meta: deltaText(deltaFor(deltas, "dms")),
      tone: deltaTone(deltaFor(deltas, "dms")),
    },
    {
      icon: "smart_toy",
      label: "Tỉ lệ tự động hóa",
      value: formatPct(replyRate),
      meta: deltaText(deltaFor(deltas, "replies")),
      tone: deltaTone(deltaFor(deltas, "replies")),
    },
    {
      icon: "timer",
      label: "Thời gian phản hồi TB",
      value: agg.avgResponseTimeSec == null ? "—" : `${agg.avgResponseTimeSec.toFixed(1)}s`,
      meta: "Từ avgResponseTimeSec",
      tone: "success" as StatusTone,
    },
    {
      icon: "payments",
      label: "Chi phí quảng cáo",
      value: formatCurrency(agg.adSpend),
      meta: deltaText(deltaFor(deltas, "adSpend")),
      tone: deltaTone(deltaFor(deltas, "adSpend"), false),
    },
  ];

  return (
    <AppShell title="Báo cáo thống kê">
      <section className="mb-gutter rounded-lg border border-primary/20 bg-primary/5 p-4">
        <div className="flex flex-col gap-3 xl:flex-row xl:items-center xl:justify-between">
          <div>
            <h1 className="text-headline-md text-secondary">Báo cáo thống kê</h1>
            <p className="mt-1 text-body-md text-on-surface-variant">Theo dõi hiệu suất và tương tác của AI Agent theo 5 kênh.</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <StatusPill tone={apiError ? "error" : omnichannelQuery.data?.stale ? "warning" : "success"}>
              {apiError ? "Analytics API lỗi" : omnichannelQuery.data?.stale ? "Dữ liệu cũ" : "Analytics online"}
            </StatusPill>
            <select
              className="rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
              value={rangePreset}
              onChange={(event) => setRangePreset(event.target.value as RangePreset)}
            >
              <option value="7d">Tuần này</option>
              <option value="30d">30 ngày</option>
            </select>
            <select
              className="rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary"
              value={platform}
              onChange={(event) => setPlatform(event.target.value)}
            >
              <option value="all">Tất cả kênh</option>
              {CHANNELS.map((channel) => <option key={channel} value={channel}>{platformLabel(channel)}</option>)}
            </select>
            <Button type="button" variant="outline" onClick={() => setExportOpen(true)}>
              <span className="material-symbols-outlined text-[18px]">download</span>
              Xuất báo cáo
            </Button>
          </div>
        </div>
      </section>

      {apiError ? (
        <div className="mb-gutter">
          <Alert tone="error">{errorMessage(apiError)}</Alert>
        </div>
      ) : null}

      <div className="mb-gutter flex flex-wrap border-b border-outline">
        {[
          ["overview", "Báo cáo Hội thoại"],
          ["agent", "Hiệu suất Agent"],
          ["lead", "Chuyển đổi Lead"],
        ].map(([value, label]) => (
          <button
            key={value}
            type="button"
            onClick={() => setTab(value as ReportTab)}
            className={`border-b-2 px-4 py-3 text-label-caps uppercase ${
              tab === value ? "border-primary text-primary" : "border-transparent text-on-surface-variant hover:text-secondary"
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      {tab === "overview" ? (
        <div className="space-y-gutter">
          <section className="grid grid-cols-1 gap-gutter sm:grid-cols-2 xl:grid-cols-4">
            {metrics.map((metric) => <MetricCard key={metric.label} {...metric} />)}
          </section>
          <section className="grid grid-cols-1 gap-gutter xl:grid-cols-[minmax(0,1.5fr)_380px]">
            <ChannelBars rows={rows} />
            <FunnelCard funnel={funnelQuery.data ?? null} />
          </section>
          <ChannelKpiGrid rows={rows} />
        </div>
      ) : null}

      {tab === "agent" ? (
        <div className="space-y-gutter">
          <section className="grid grid-cols-1 gap-gutter sm:grid-cols-2 xl:grid-cols-4">
            <MetricCard
              icon="fact_check"
              label="Chất lượng trung bình"
              value={averageQualityPassRate == null ? "—" : formatPct(averageQualityPassRate * 100)}
              meta={qualitySamples ? `${formatNumber(qualitySamples)} quality samples` : "Chưa có mẫu quality"}
              tone={averageQualityPassRate == null ? "neutral" : averageQualityPassRate >= 0.85 ? "success" : "warning"}
            />
            <MetricCard icon="speed" label="Tác vụ hoàn tất" value={formatNumber(agents.reduce((sum, agent) => sum + agent.completedSessions, 0))} meta="completedSessions" tone="success" />
            <MetricCard icon="toll" label="Chi phí token" value={formatUsd(costs.reduce((sum, cost) => sum + cost.usd, 0))} meta="Claude cost ledger" tone="warning" />
            <MetricCard icon="bug_report" label="Trace count" value={formatNumber(agents.reduce((sum, agent) => sum + agent.traceCount, 0))} meta="Agent traces" tone="neutral" />
          </section>
          <section className="grid grid-cols-1 gap-gutter xl:grid-cols-[minmax(0,1fr)_360px]">
            <AgentTable agents={agents} costs={costs} />
            <AgentRadar agents={agents} />
          </section>
        </div>
      ) : null}

      {tab === "lead" ? (
        <div className="space-y-gutter">
          <section className="grid grid-cols-1 gap-gutter sm:grid-cols-2 xl:grid-cols-4">
            <MetricCard icon="person_add" label="Lead" value={formatNumber(agg.leads)} meta={deltaText(deltaFor(deltas, "leads"))} tone={deltaTone(deltaFor(deltas, "leads"))} />
            <MetricCard icon="moving" label="Chuyển đổi" value={formatNumber(agg.conversions)} meta={formatPct(conversionRate)} tone="success" />
            <MetricCard icon="paid" label="CPL trung bình" value={agg.leads ? formatCurrency(agg.adSpend / agg.leads) : "—"} meta="adSpend / leads" tone="warning" />
            <MetricCard icon="forum" label="Reply rate" value={formatPct(replyRate)} meta="replies / dms" tone="success" />
          </section>
          <section className="grid grid-cols-1 gap-gutter xl:grid-cols-[minmax(0,1fr)_380px]">
            <ForecastCard points={forecast} />
            <AnomalyList anomalies={anomalies} />
          </section>
          <ChannelKpiGrid rows={rows} />
        </div>
      ) : null}

      <ExportDialog
        open={exportOpen}
        exporting={exportMutation.isPending}
        error={exportMutation.error}
        onClose={() => setExportOpen(false)}
        onExport={(format) => exportMutation.mutate(format)}
      />
    </AppShell>
  );
}
