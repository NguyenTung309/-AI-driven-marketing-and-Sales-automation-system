import { Card } from "@/shared/ui/Card";
import type { ReportPayload, ReportRow } from "@/shared/api/reports";

const WIDTH = 680;
const HEIGHT = 250;
const PAD_LEFT = 48;
const PAD_RIGHT = 16;
const PAD_TOP = 22;
const PAD_BOTTOM = 54;
const PLOT_W = WIDTH - PAD_LEFT - PAD_RIGHT;
const PLOT_H = HEIGHT - PAD_TOP - PAD_BOTTOM;
const MAX_X_LABELS = 8;

// Bảng màu bám theo accent đỏ của analytics; 5 màu là đủ vì payload hiện tối đa 3 series.
const SERIES_COLORS = ["#d32f2f", "#1e88e5", "#43a047", "#f9a825", "#8e24aa"];

const PLATFORM_LABELS: Record<string, string> = {
  all: "Tất cả",
  all_platforms: "Tất cả",
  facebook: "Facebook",
  instagram: "Instagram",
  zalo: "Zalo",
  tiktok: "TikTok",
  youtube: "YouTube",
  website: "Website",
  unknown: "Khác",
};

function toNumber(row: ReportRow, key: string): number | null {
  const value = row[key];
  if (typeof value === "number" && Number.isFinite(value)) return value;
  if (typeof value === "string" && value.trim() !== "" && Number.isFinite(Number(value))) return Number(value);
  return null;
}

function formatValue(value: number): string {
  return value.toLocaleString("vi-VN", { maximumFractionDigits: 2 });
}

function formatAxisLabel(value: string, isDate: boolean): string {
  if (!isDate) {
    const key = value.trim().toLowerCase();
    return PLATFORM_LABELS[key] ?? (value ? value.charAt(0).toUpperCase() + value.slice(1) : "—");
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("vi-VN", { day: "2-digit", month: "2-digit" }).format(date);
}

interface ChartModel {
  readonly labels: readonly string[];
  readonly isDateAxis: boolean;
  readonly xTitle: string;
  readonly series: readonly { readonly key: string; readonly label: string; readonly values: readonly (number | null)[] }[];
  readonly min: number;
  readonly max: number;
}

function buildModel(payload: ReportPayload): ChartModel | null {
  const chart = payload.chart;
  if (!chart || payload.rows.length === 0) return null;

  const xColumn = payload.columns.find((c) => c.key === chart.x);
  const series = chart.series
    .map((key) => ({
      key,
      label: payload.columns.find((c) => c.key === key)?.label ?? key,
      values: payload.rows.map((row) => toNumber(row, key)),
    }))
    .filter((s) => s.values.some((v) => v !== null));

  if (series.length === 0) return null;

  const numbers = series.flatMap((s) => s.values.filter((v): v is number => v !== null));
  const rawMin = Math.min(...numbers);
  const rawMax = Math.max(...numbers);
  // Cột luôn mọc từ 0, còn đường thì bám sát vùng giá trị để biến động nhỏ vẫn nhìn thấy được.
  const isDateAxis = xColumn?.type === "date";
  const min = isDateAxis ? Math.min(rawMin, rawMax === rawMin ? rawMin - 1 : rawMin) : Math.min(0, rawMin);
  const max = rawMax === min ? min + 1 : rawMax;
  const xTitle = xColumn?.label ?? (isDateAxis ? "Thời gian (Ngày)" : "Nền tảng");

  return {
    labels: payload.rows.map((row) => String(row[chart.x] ?? "")),
    isDateAxis,
    xTitle,
    series,
    min,
    max,
  };
}

function yOf(value: number, model: ChartModel): number {
  const span = model.max - model.min || 1;
  return PAD_TOP + PLOT_H - ((value - model.min) / span) * PLOT_H;
}

function Gridlines({ model }: { readonly model: ChartModel }) {
  const ticks = [0, 0.5, 1].map((f) => model.min + (model.max - model.min) * f);
  const baseline = yOf(Math.max(model.min, 0), model);
  return (
    <g>
      <text
        x={PAD_LEFT - 6}
        y={PAD_TOP - 8}
        textAnchor="end"
        className="fill-on-surface-variant text-[9px] font-medium"
      >
        Số lượng
      </text>
      {ticks.map((tick) => (
        <g key={tick}>
          <line
            x1={PAD_LEFT}
            x2={WIDTH - PAD_RIGHT}
            y1={yOf(tick, model)}
            y2={yOf(tick, model)}
            stroke="#e2e8f0"
            strokeWidth="1"
          />
          <text
            x={PAD_LEFT - 8}
            y={yOf(tick, model) + 3}
            textAnchor="end"
            className="fill-on-surface-variant text-[10px]"
          >
            {formatValue(tick)}
          </text>
        </g>
      ))}
      <line
        x1={PAD_LEFT}
        x2={WIDTH - PAD_RIGHT}
        y1={baseline}
        y2={baseline}
        stroke="#cbd5e1"
        strokeWidth="1.5"
      />
    </g>
  );
}

function XLabels({ model }: { readonly model: ChartModel }) {
  const step = Math.max(1, Math.ceil(model.labels.length / MAX_X_LABELS));
  const slot = PLOT_W / model.labels.length;
  const baseline = yOf(Math.max(model.min, 0), model);

  return (
    <g>
      {model.labels.map((label, index) => {
        if (index % step !== 0) return null;
        const x = PAD_LEFT + slot * (index + 0.5);
        return (
          <g key={`${label}-${index}`}>
            <line
              x1={x}
              x2={x}
              y1={baseline}
              y2={baseline + 4}
              stroke="#cbd5e1"
              strokeWidth="1"
            />
            <text
              x={x}
              y={baseline + 16}
              textAnchor="middle"
              className="fill-on-surface-variant text-[10px] font-medium"
            >
              {formatAxisLabel(label, model.isDateAxis)}
            </text>
          </g>
        );
      })}
      <text
        x={PAD_LEFT + PLOT_W / 2}
        y={HEIGHT - 8}
        textAnchor="middle"
        className="fill-on-surface-variant text-[11px] font-semibold tracking-wide"
      >
        {model.xTitle}
      </text>
    </g>
  );
}

function LineSeries({ model }: { readonly model: ChartModel }) {
  const slot = PLOT_W / model.labels.length;
  return (
    <g>
      {model.series.map((series, seriesIndex) => {
        const color = SERIES_COLORS[seriesIndex % SERIES_COLORS.length];
        const points = series.values
          .map((value, index) => (value === null ? null : `${PAD_LEFT + slot * (index + 0.5)},${yOf(value, model)}`))
          .filter((p): p is string => p !== null);
        return (
          <g key={series.key}>
            <polyline
              points={points.join(" ")}
              fill="none"
              stroke={color}
              strokeWidth="2.5"
              strokeLinecap="round"
              strokeLinejoin="round"
            />
            {series.values.map((value, index) =>
              value === null ? null : (
                <circle
                  key={`${series.key}-${index}`}
                  cx={PAD_LEFT + slot * (index + 0.5)}
                  cy={yOf(value, model)}
                  r="3.5"
                  fill={color}
                >
                  <title>{`${formatAxisLabel(model.labels[index], model.isDateAxis)} • ${series.label}: ${formatValue(value)}`}</title>
                </circle>
              )
            )}
          </g>
        );
      })}
    </g>
  );
}

function BarSeries({ model }: { readonly model: ChartModel }) {
  const slot = PLOT_W / model.labels.length;
  const barWidth = Math.min(26, (slot * 0.72) / model.series.length);
  const groupWidth = barWidth * model.series.length;
  const baseline = yOf(Math.max(model.min, 0), model);

  return (
    <g>
      {model.series.map((series, seriesIndex) => {
        const color = SERIES_COLORS[seriesIndex % SERIES_COLORS.length];
        return (
          <g key={series.key}>
            {series.values.map((value, index) => {
              if (value === null) return null;
              const x = PAD_LEFT + slot * (index + 0.5) - groupWidth / 2 + barWidth * seriesIndex;
              const y = yOf(value, model);
              const barHeight = Math.max(1, Math.abs(baseline - y));
              const topY = Math.min(y, baseline);

              return (
                <g key={`${series.key}-${index}`}>
                  <rect
                    x={x}
                    y={topY}
                    width={Math.max(2, barWidth - 3)}
                    height={barHeight}
                    rx="3"
                    fill={color}
                  >
                    <title>{`${formatAxisLabel(model.labels[index], model.isDateAxis)} • ${series.label}: ${formatValue(value)}`}</title>
                  </rect>
                  {barWidth >= 14 && value > 0 && (
                    <text
                      x={x + (barWidth - 3) / 2}
                      y={topY - 3}
                      textAnchor="middle"
                      className="fill-on-surface-variant text-[8px] font-medium"
                    >
                      {formatValue(value)}
                    </text>
                  )}
                </g>
              );
            })}
          </g>
        );
      })}
    </g>
  );
}

/**
 * Biểu đồ cho artifact báo cáo. Cột x kiểu date vẽ đường (xu hướng theo thời gian), còn lại vẽ cột
 * (so sánh giữa các nhóm) — payload đã chuẩn hoá nên một component phục vụ được cả ba loại báo cáo.
 */
export function ReportChartCard({ payload }: { readonly payload: ReportPayload }) {
  const model = buildModel(payload);
  if (!model) return null;

  return (
    <Card>
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-headline-sm text-secondary">Biểu đồ</h2>
        <div className="flex flex-wrap items-center gap-4">
          {model.series.map((series, index) => (
            <span key={series.key} className="flex items-center gap-2 text-label-sm text-on-surface-variant">
              <span
                className="size-2.5 rounded-full"
                style={{ backgroundColor: SERIES_COLORS[index % SERIES_COLORS.length] }}
                aria-hidden="true"
              />
              {series.label}
            </span>
          ))}
        </div>
      </div>
      <div className="overflow-x-auto">
        <svg viewBox={`0 0 ${WIDTH} ${HEIGHT}`} className="min-w-[520px] w-full" role="img" aria-label="Biểu đồ báo cáo">
          <Gridlines model={model} />
          {model.isDateAxis ? <LineSeries model={model} /> : <BarSeries model={model} />}
          <XLabels model={model} />
        </svg>
      </div>
    </Card>
  );
}
