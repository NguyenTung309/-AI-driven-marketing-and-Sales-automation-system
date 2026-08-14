import { apiClient } from "./client";
import type { JobAccepted } from "./jobs";

/** Kiểu dữ liệu của một trường trong biểu mẫu tạo tài liệu. */
export type TemplateFieldType = "text" | "multiline" | "number" | "currency" | "date";

export interface TemplateField {
  readonly key: string;
  readonly label: string;
  readonly type: TemplateFieldType;
  readonly required: boolean;
  readonly placeholder: string | null;
  readonly sample: string | null;
}

export interface DocumentTemplate {
  readonly id: string;
  readonly code: string;
  readonly docType: string;
  readonly templateHtml: string;
  readonly fields: readonly TemplateField[];
  readonly createdAt: string;
  readonly updatedAt: string;
}

export interface GeneratedDocument {
  readonly id: string;
  readonly templateId: string;
  readonly contactId: string | null;
  readonly fileUrl: string;
  readonly fileHash: string | null;
  readonly sentVia: string | null;
  readonly sentAt: string | null;
  readonly openedAt: string | null;
  readonly createdAt: string;
  readonly expiresAt: string | null;
}

export interface GenerateDocumentPayload {
  readonly templateCode: string;
  readonly contactId?: string | null;
  readonly vars?: Record<string, string> | null;
  readonly sentVia?: string | null;
}

export interface GenerateDocumentResponse {
  readonly documentId: string;
  readonly fileUrl: string;
  readonly fileHash: string;
  readonly sizeBytes: number;
  readonly latencyMs: number;
}

export interface GenerateDocumentKitPayload {
  readonly templateCodes?: readonly string[] | null;
  readonly contactId?: string | null;
  readonly vars?: Record<string, string> | null;
  readonly sentVia?: string | null;
}

export interface GenerateDocumentKitResponse {
  readonly documents: readonly GenerateDocumentResponse[];
  readonly totalSizeBytes: number;
  readonly totalLatencyMs: number;
}

export interface DocumentTemplatePayload {
  readonly code: string;
  readonly docType: string;
  readonly templateHtml: string;
  readonly fields?: readonly TemplateField[] | null;
}

export interface DocumentListResponse<T> {
  readonly items: readonly T[];
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
}

/** API cũ có thể không trả fields; chuẩn hoá để UI luôn nhận mảng. */
function normalizeTemplate(tpl: DocumentTemplate): DocumentTemplate {
  return Array.isArray(tpl.fields) ? tpl : { ...tpl, fields: [] };
}

export async function listDocumentTemplates(
  params?: { readonly page?: number; readonly pageSize?: number },
): Promise<DocumentListResponse<DocumentTemplate>> {
  const res = await apiClient.get<DocumentListResponse<DocumentTemplate> | readonly DocumentTemplate[]>(
    "/api/docs/templates",
    { params },
  );
  const data = res.data as DocumentListResponse<DocumentTemplate> | readonly DocumentTemplate[];
  if (Array.isArray(data)) {
    const items = data.map(normalizeTemplate);
    return { items, total: items.length, page: 1, pageSize: items.length || 50 };
  }
  const page = data as DocumentListResponse<DocumentTemplate>;
  return { ...page, items: (page.items ?? []).map(normalizeTemplate) };
}

export async function createDocumentTemplate(payload: DocumentTemplatePayload): Promise<DocumentTemplate> {
  const res = await apiClient.post<DocumentTemplate>("/api/docs/templates", payload);
  return normalizeTemplate(res.data);
}

export async function updateDocumentTemplate(id: string, payload: Omit<DocumentTemplatePayload, "code">): Promise<void> {
  await apiClient.put(`/api/docs/templates/${id}`, payload);
}

export async function deleteDocumentTemplate(id: string): Promise<void> {
  await apiClient.delete(`/api/docs/templates/${id}`);
}

export async function listGeneratedDocuments(
  params?: { readonly page?: number; readonly pageSize?: number },
): Promise<DocumentListResponse<GeneratedDocument>> {
  const res = await apiClient.get<DocumentListResponse<GeneratedDocument> | readonly GeneratedDocument[]>(
    "/api/docs/generated",
    { params },
  );
  const data = res.data as DocumentListResponse<GeneratedDocument> | readonly GeneratedDocument[];
  if (Array.isArray(data)) {
    return { items: data, total: data.length, page: 1, pageSize: data.length || 50 };
  }
  return data as DocumentListResponse<GeneratedDocument>;
}

export async function generateDocument(payload: GenerateDocumentPayload): Promise<JobAccepted> {
  const res = await apiClient.post<JobAccepted>("/api/docs/generate", payload);
  return res.data;
}

export async function generateDocumentKit(payload: GenerateDocumentKitPayload): Promise<JobAccepted> {
  const res = await apiClient.post<JobAccepted>("/api/docs/generate-kit", payload);
  return res.data;
}

export function documentDownloadUrl(id: string): string {
  return `/api/docs/${id}/download`;
}

/**
 * Tải PDF qua apiClient để interceptor gắn Bearer token. Không dùng trực tiếp `fileUrl` hay đặt
 * đường dẫn này vào <a href>/<iframe src>: điều hướng thuần của trình duyệt không mang token, và
 * `/generated-docs/...` không được host nào phục vụ nên rơi vào SPA fallback rồi báo "404 Not Found".
 */
export async function fetchGeneratedDocumentBlob(id: string): Promise<Blob> {
  const res = await apiClient.get<Blob>(documentDownloadUrl(id), { responseType: "blob" });
  return res.data;
}
