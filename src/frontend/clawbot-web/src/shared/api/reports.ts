import { apiClient } from "./client";

export type ReportKind =
  | "snapshot"
  | "anomaly"
  | "forecast"
  | "content_snapshot"
  | "content_funnel";
export type ReportColumnType = "text" | "number" | "date";
export type ReportExportFormat = "csv" | "xlsx" | "pdf";

export interface ReportColumn {
  readonly key: string;
  readonly label: string;
  readonly type: ReportColumnType;
}

export interface ReportChart {
  readonly x: string;
  readonly series: readonly string[];
}

export type ReportCell = string | number | boolean | null;
export type ReportRow = Readonly<Record<string, ReportCell>>;

export interface ReportPayload {
  readonly kind: ReportKind;
  readonly columns: readonly ReportColumn[];
  readonly rows: readonly ReportRow[];
  readonly chart: ReportChart | null;
}

export interface ReportSummary {
  readonly id: string;
  readonly kind: ReportKind;
  readonly title: string;
  readonly platform: string;
  readonly metric: string | null;
  readonly fromDate: string;
  readonly toDate: string;
  readonly createdAt: string;
}

export interface ReportDetail extends ReportSummary {
  readonly data: ReportPayload;
}

export interface ReportListResponse {
  readonly total: number;
  readonly items: readonly ReportSummary[];
}

// GET /api/reports?limit=
export async function listReports(limit?: number): Promise<ReportListResponse> {
  const res = await apiClient.get<ReportListResponse>("/api/reports", { params: { limit } });
  return res.data;
}

// GET /api/reports/{id}
export async function getReport(id: string): Promise<ReportDetail> {
  const res = await apiClient.get<ReportDetail>(`/api/reports/${id}`);
  return res.data;
}

// GET /api/reports/{id}/export?format=
export async function downloadReportExport(id: string, format: ReportExportFormat): Promise<Blob> {
  const res = await apiClient.get<Blob>(`/api/reports/${id}/export`, {
    params: { format },
    responseType: "blob",
  });
  return res.data;
}
