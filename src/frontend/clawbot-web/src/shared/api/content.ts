import { apiClient } from "./client";

export interface ContentBrief {
  readonly id: string;
  readonly platform: string;
  readonly brief: string;
  readonly status: string;
  readonly createdBy: string | null;
  readonly createdAt: string;
  readonly updatedAt: string;
}

export interface ContentItem {
  readonly id: string;
  readonly briefId: string | null;
  readonly platform: string;
  readonly status: string;
  readonly body: string;
  readonly assetsJson: string;
  readonly createdBy: string | null;
  readonly approvedBy: string | null;
  readonly approvedAt: string | null;
  readonly createdAt: string;
  readonly updatedAt: string;
}

export interface ContentQueueResponse {
  readonly items: readonly ContentItem[];
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
}

export interface ContentSchedule {
  readonly id: string;
  readonly contentItemId: string;
  readonly platform: string;
  readonly scheduledAt: string;
  readonly postedAt: string | null;
  readonly status: string;
  readonly postUrl: string | null;
  readonly createdAt: string;
  readonly updatedAt: string;
}

export interface ContentCalendarItem {
  readonly scheduleId: string;
  readonly contentItemId: string;
  readonly platform: string;
  readonly status: string;
  readonly body: string;
  readonly scheduledAt: string;
  readonly postedAt: string | null;
  readonly postUrl: string | null;
}

export interface ContentCalendarResponse {
  readonly items: readonly ContentCalendarItem[];
}

export interface Trend {
  readonly topic: string;
  readonly source: string;
  readonly metric: string;
  readonly relevanceScore: number;
  readonly contentIdeas: readonly string[];
}

export interface TrendScanResponse {
  readonly trends: readonly Trend[];
}

export interface ContentBriefPayload {
  readonly platform: string;
  readonly brief: string;
}

export interface GenerateContentItemPayload {
  readonly briefId?: string | null;
  readonly platform?: string | null;
  readonly briefText?: string | null;
}

export interface GenerateImagePromptPayload {
  readonly brief?: string | null;
  readonly platform?: string | null;
  readonly style?: string | null;
  readonly brandTokens?: readonly string[] | null;
}

export interface GenerateImagePromptResponse {
  readonly prompt: string;
  readonly negativePrompt: string;
  readonly hints: Readonly<Record<string, string>>;
}

export interface UpdateContentItemPayload {
  readonly body: string;
  readonly assetsJson?: string | null;
}

export interface ContentQueueParams {
  readonly status?: string;
  readonly platform?: string;
  readonly page?: number;
  readonly pageSize?: number;
}

export interface ContentCalendarParams {
  readonly from?: string;
  readonly to?: string;
}

function cleanParams(params: object): Record<string, string | number> {
  const cleaned: Record<string, string | number> = {};
  for (const [key, value] of Object.entries(params)) {
    if (typeof value === "number" || (typeof value === "string" && value !== "")) {
      cleaned[key] = value;
    }
  }
  return cleaned;
}

export async function listContentBriefs(params: { readonly status?: string; readonly platform?: string } = {}): Promise<readonly ContentBrief[]> {
  const res = await apiClient.get<readonly ContentBrief[]>("/api/content/briefs", { params: cleanParams(params) });
  return res.data;
}

export async function createContentBrief(payload: ContentBriefPayload): Promise<ContentBrief> {
  const res = await apiClient.post<ContentBrief>("/api/content/briefs", payload);
  return res.data;
}

export async function updateContentBrief(id: string, payload: ContentBriefPayload): Promise<ContentBrief> {
  const res = await apiClient.put<ContentBrief>(`/api/content/briefs/${id}`, payload);
  return res.data;
}

export async function deleteContentBrief(id: string): Promise<void> {
  await apiClient.delete(`/api/content/briefs/${id}`);
}

export async function getContentTrends(week?: string): Promise<TrendScanResponse> {
  const res = await apiClient.get<TrendScanResponse>("/api/content/trends", { params: cleanParams({ week }) });
  return res.data;
}

export async function scanContentTrends(week?: string): Promise<TrendScanResponse> {
  const res = await apiClient.post<TrendScanResponse>("/api/content/trends/scan", null, { params: cleanParams({ week }) });
  return res.data;
}

export async function generateContentItems(payload: GenerateContentItemPayload): Promise<{ readonly items: readonly ContentItem[] }> {
  const res = await apiClient.post<{ readonly items: readonly ContentItem[] }>("/api/content/items/generate", payload);
  return res.data;
}

export async function generateImagePrompt(payload: GenerateImagePromptPayload): Promise<GenerateImagePromptResponse> {
  const res = await apiClient.post<GenerateImagePromptResponse>("/api/content/image-prompts", payload);
  return res.data;
}

export async function getContentQueue(params: ContentQueueParams = {}): Promise<ContentQueueResponse> {
  const res = await apiClient.get<ContentQueueResponse>("/api/content/queue", { params: cleanParams(params) });
  return res.data;
}

export async function updateContentItem(id: string, payload: UpdateContentItemPayload): Promise<ContentItem> {
  const res = await apiClient.put<ContentItem>(`/api/content/items/${id}`, payload);
  return res.data;
}

export async function deleteContentItem(id: string): Promise<void> {
  await apiClient.delete(`/api/content/items/${id}`);
}

export async function approveContentItem(id: string): Promise<ContentItem> {
  const res = await apiClient.post<ContentItem>(`/api/content/items/${id}/approve`);
  return res.data;
}

export async function rejectContentItem(id: string, reason?: string): Promise<ContentItem> {
  const res = await apiClient.post<ContentItem>(`/api/content/items/${id}/reject`, { reason: reason?.trim() || null });
  return res.data;
}

export async function scheduleContentItem(id: string, scheduledAt: string | null): Promise<ContentSchedule> {
  const res = await apiClient.post<ContentSchedule>(`/api/content/items/${id}/schedule`, { scheduledAt });
  return res.data;
}

export async function repurposeContentItem(id: string, targetPlatforms: readonly string[]): Promise<{ readonly items: readonly ContentItem[] }> {
  const res = await apiClient.post<{ readonly items: readonly ContentItem[] }>(`/api/content/items/${id}/repurpose`, {
    targetPlatforms,
  });
  return res.data;
}

export async function getContentCalendar(params: ContentCalendarParams = {}): Promise<ContentCalendarResponse> {
  const res = await apiClient.get<ContentCalendarResponse>("/api/content/calendar", { params: cleanParams(params) });
  return res.data;
}

export async function deleteContentSchedule(id: string): Promise<void> {
  await apiClient.delete(`/api/content/schedule/${id}`);
}
