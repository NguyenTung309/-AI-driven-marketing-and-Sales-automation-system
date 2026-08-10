import { useMemo } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Link, useParams } from "react-router-dom";
import { AppShell } from "@/shared/layout/AppShell";
import { Alert } from "@/shared/ui/Alert";
import { Button } from "@/shared/ui/Button";
import { Card } from "@/shared/ui/Card";
import { DataTable, type Column } from "@/shared/ui/DataTable";
import { StatusPill, type StatusTone } from "@/shared/ui/StatusPill";
import { toUserFriendlyError } from "@/shared/utils/userText";
import {
  downloadReportExport,
  getReport,
  type ReportCell,
  type ReportColumn,
  type ReportExportFormat,
  type ReportKind,
  type ReportRow,
} from "@/shared/api/reports";
import { ReportChartCard } from "./ReportChartCard";

const KIND_LABEL: Record<ReportKind, string> = {
  snapshot: "Tổng hợp KPI",
  anomaly: "Phát hiện bất thường",
  forecast: "Dự báo",
};

const KIND_TONE: Record<ReportKind, StatusTone> = {
  snapshot: "neutral",
  anomaly: "warning",
  forecast: "success",
};

interface TableRow {
  readonly key: string;
  readonly cells: ReportRow;
}

const EXPORTS: readonly { readonly format: ReportExportFormat; readonly label: string }[] = [
  { format: "xlsx", label: "Tải Excel" },
  { format: "csv", label: "Tải CSV" },
  { format: "pdf", label: "Tải PDF" },
];

function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(url);
}

function formatDate(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("vi-VN", { day: "2-digit", month: "2-digit", year: "numeric" }).format(date);
}

function formatDateTime(value: string): string {
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

function formatCell(value: ReportCell, column: ReportColumn): string {
  if (value === null || value === undefined || value === "") return "—";
  if (typeof value === "boolean") return value ? "Có" : "Không";
  if (column.type === "date") return formatDate(String(value));
  if (column.type === "number") {
    const numeric = typeof value === "number" ? value : Number(value);
    if (!Number.isFinite(numeric)) return String(value);
    return numeric.toLocaleString("vi-VN", { maximumFractionDigits: 2 });
  }
  return String(value);
}

export default function ReportDetailPage() {
  const { reportId = "" } = useParams<{ reportId: string }>();

  const reportQuery = useQuery({
    queryKey: ["reports", "detail", reportId],
    queryFn: () => getReport(reportId),
    enabled: Boolean(reportId),
  });

  const exportMutation = useMutation({
    mutationFn: (format: ReportExportFormat) => downloadReportExport(reportId, format),
    onSuccess: (blob, format) => downloadBlob(blob, `bao-cao-${reportId.slice(0, 8)}.${format}`),
  });

  const report = reportQuery.data;
  const payload = report?.data;

  const columns = useMemo<readonly Column<TableRow>[]>(
    () =>
      (payload?.columns ?? []).map((column) => ({
        key: column.key,
        header: column.label,
        className: column.type === "number" ? "text-right tabular-nums" : "",
        render: (row: TableRow) => formatCell(row.cells[column.key], column),
      })),
    [payload]
  );

  // Khoá theo vị trí: payload không có id dòng, và nội dung dòng có thể trùng nhau (cùng platform, cùng số).
  const rows = useMemo<readonly TableRow[]>(
    () => (payload?.rows ?? []).map((cells, index) => ({ key: String(index), cells })),
    [payload]
  );

  return (
    <AppShell title="Chi tiết báo cáo">
      <div className="mb-stack-lg flex flex-col gap-2">
        <Link className="text-label-sm text-primary hover:underline" to="/analytics">
          ← Quay lại Báo cáo &amp; Phân tích
        </Link>
      </div>

      {reportQuery.isLoading ? (
        <Card>
          <p className="text-body-md text-on-surface-variant">Đang tải báo cáo…</p>
        </Card>
      ) : null}

      {reportQuery.isError ? (
        <Alert tone="error">Không mở được báo cáo: {toUserFriendlyError(reportQuery.error)}</Alert>
      ) : null}

      {report && payload ? (
        <div className="flex flex-col gap-stack-lg">
          <Card>
            <div className="flex flex-wrap items-start justify-between gap-4">
              <div>
                <div className="flex flex-wrap items-center gap-3">
                  <h1 className="text-headline-md text-secondary">{report.title}</h1>
                  <StatusPill tone={KIND_TONE[report.kind] ?? "neutral"}>
                    {KIND_LABEL[report.kind] ?? report.kind}
                  </StatusPill>
                </div>
                <p className="mt-2 text-body-md text-on-surface-variant">
                  Khoảng dữ liệu {formatDate(report.fromDate)} – {formatDate(report.toDate)} · Nền tảng{" "}
                  {report.platform === "all" ? "tất cả" : report.platform}
                  {report.metric ? ` · Chỉ số ${report.metric}` : ""}
                </p>
                <p className="mt-1 text-label-sm text-on-surface-variant">
                  Chốt lúc {formatDateTime(report.createdAt)} · số liệu giữ nguyên tại thời điểm chạy.
                </p>
              </div>
              <div className="flex flex-wrap gap-2">
                {EXPORTS.map((item) => (
                  <Button
                    key={item.format}
                    variant="outline"
                    disabled={exportMutation.isPending || rows.length === 0}
                    onClick={() => exportMutation.mutate(item.format)}
                  >
                    {item.label}
                  </Button>
                ))}
              </div>
            </div>
            {exportMutation.isError ? (
              <div className="mt-4">
                <Alert tone="error">Tải file thất bại: {toUserFriendlyError(exportMutation.error)}</Alert>
              </div>
            ) : null}
          </Card>

          <ReportChartCard payload={payload} />

          <Card>
            <h2 className="mb-4 text-headline-sm text-secondary">Số liệu chi tiết ({rows.length} dòng)</h2>
            <DataTable
              columns={columns}
              rows={rows}
              rowKey={(row) => row.key}
              empty="Báo cáo không có dòng dữ liệu nào."
            />
          </Card>
        </div>
      ) : null}
    </AppShell>
  );
}
