import { apiClient } from "./client";
import type { JobAccepted } from "./jobs";

export interface ContentBrief {
  readonly id: string;
  readonly platform: string;
  readonly brief: string;
  readonly status: string;
  readonly createdBy: string | null;
  readonly createdAt: string;
  readonly updatedAt: string;
}

export interface ContentAgentReview {
  readonly status: string;
  readonly reviewedRevision: number | null;
  readonly reviewedByAgentId: string | null;
  readonly reviewedAt: string | null;
  readonly reason: string | null;
  readonly imageReviewStatus: string;
  readonly reviewedImageCount: number;
}

export interface ContentPublishingApproval {
  readonly status: string;
  readonly policyApplied: string | null;
  readonly policyVersionApplied: number | null;
  readonly approvedRevision: number | null;
  readonly mode: string | null;
  readonly approvedBy: string | null;
  readonly approvedAt: string | null;
  readonly reason: string | null;
  readonly requirementReason: string | null;
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
  readonly contentRevision?: number;
  readonly agentReview?: ContentAgentReview | null;
  readonly publishingApproval?: ContentPublishingApproval | null;
  readonly workflowState?: string;
  readonly canApprove?: boolean;
  readonly canReject?: boolean;
  readonly canRetryReview?: boolean;
  readonly canSchedule?: boolean;
  readonly canPublish?: boolean;
}

export type ContentPublishingApprovalPolicy = "automatic" | "human_required";
export type ContentReviewerVisionCapability = "available" | "unavailable" | "unknown";

export interface ContentPublishingPolicy {
  readonly agentReviewRequired: boolean;
  readonly agentReviewMode: string;
  readonly reviewerVisionCapability: ContentReviewerVisionCapability | string;
  readonly publishingApprovalPolicy: ContentPublishingApprovalPolicy | string;
  readonly policyVersion: number;
  readonly updatedAt: string;
}

export interface UpdateContentPublishingPolicyPayload {
  readonly publishingApprovalPolicy: ContentPublishingApprovalPolicy;
}

export interface ApproveContentItemPayload {
  readonly expectedRevision: number;
  readonly overrideReason?: string | null;
}

export interface RejectContentItemPayload {
  readonly expectedRevision: number;
  readonly reason?: string | null;
}

export interface RetryAgentReviewPayload {
  readonly expectedRevision: number;
}

export interface ReconcilePublishPayload {
  readonly outcome: "succeeded" | "failed";
  readonly externalPostId?: string | null;
  readonly errorCode?: string | null;
}

export interface ContentQueueResponse {
  readonly items: readonly ContentItem[];
  readonly total?: number | null;
  readonly page?: number;
  readonly pageSize?: number;
  readonly nextCursor?: string | null;
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
  readonly metaAssetId: string | null;
  readonly likeCount: number | null;
  readonly commentCount: number | null;
  readonly engagementSyncedAt: string | null;
  readonly retryCount: number;
  readonly lastError: string | null;
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
  readonly metaAssetId: string | null;
  readonly likeCount: number | null;
  readonly commentCount: number | null;
  readonly retryCount: number;
  readonly lastError: string | null;
  readonly requiresInstagramAccountConfirmation: boolean;
}

export interface ContentPublishTarget {
  readonly id: string;
  readonly platform: string;
  readonly externalId: string;
  readonly name: string;
  readonly isDefault: boolean;
}

export type ContentPublishTargetMode = "linked_meta" | "standalone" | "invalid" | "unsupported";

export interface ContentPublishTargetsResponse {
  readonly mode: ContentPublishTargetMode;
  readonly items: readonly ContentPublishTarget[];
}

export interface ScheduleContentItemPayload {
  readonly scheduledAt: string | null;
  readonly metaAssetId?: string | null;
  readonly confirmInstagramAccount?: boolean;
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
  readonly weekOf: string;
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

export interface ContentAssetUploadResponse {
  readonly url: string;
  readonly assetsJson: string;
}

export interface UpdateContentItemPayload {
  readonly body: string;
  readonly assetsJson?: string | null;
}

export interface ContentQueueParams {
  readonly status?: string;
  readonly platform?: string;
  readonly cursor?: string | null;
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

export interface ContentBriefListResponse {
  readonly items: readonly ContentBrief[];
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
}

export async function listContentBriefs(
  params: { readonly status?: string; readonly platform?: string; readonly page?: number; readonly pageSize?: number } = {},
): Promise<ContentBriefListResponse> {
  const res = await apiClient.get<ContentBriefListResponse | readonly ContentBrief[]>("/api/content/briefs", {
    params: cleanParams(params),
  });
  const data = res.data as ContentBriefListResponse | readonly ContentBrief[];
  if (Array.isArray(data)) {
    return { items: data, total: data.length, page: 1, pageSize: data.length || 50 };
  }
  return data as ContentBriefListResponse;
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

// Chạy ngầm: trả jobId; kết quả xu hướng về qua thông báo + realtime.
export async function scanContentTrends(week?: string): Promise<JobAccepted> {
  const res = await apiClient.post<JobAccepted>("/api/content/trends/scan", null, { params: cleanParams({ week }) });
  return res.data;
}

export interface TrendSourceSetting {
  readonly enabled: boolean;
  readonly hasApiKey: boolean;
  readonly url?: string | null;
}

export interface TrendScheduleInfo {
  readonly cadence: string;
  readonly nextRunAt?: string | null;
  readonly lastRunAt?: string | null;
}

export interface TrendSettings {
  readonly geo: string;
  readonly google: TrendSourceSetting;
  readonly youTube: TrendSourceSetting;
  readonly tikTok: TrendSourceSetting;
  readonly schedule: TrendScheduleInfo;
}

/** apiKey/url: null = giữ giá trị đã lưu, chuỗi rỗng = xoá. */
export interface UpdateTrendSourcePayload {
  readonly enabled?: boolean | null;
  readonly apiKey?: string | null;
  readonly url?: string | null;
}

export interface UpdateTrendSettingsPayload {
  readonly geo?: string | null;
  readonly google?: UpdateTrendSourcePayload | null;
  readonly youTube?: UpdateTrendSourcePayload | null;
  readonly tikTok?: UpdateTrendSourcePayload | null;
  readonly scheduleCadence?: string | null;
}

export async function getTrendSettings(): Promise<TrendSettings> {
  const res = await apiClient.get<TrendSettings>("/api/content/trends/settings");
  return res.data;
}

export async function updateTrendSettings(payload: UpdateTrendSettingsPayload): Promise<TrendSettings> {
  const res = await apiClient.put<TrendSettings>("/api/content/trends/settings", payload);
  return res.data;
}

// Chạy ngầm: trả jobId, kết quả về qua thông báo (link tới bài trong hàng đợi).
export async function generateContentItems(payload: GenerateContentItemPayload): Promise<JobAccepted> {
  const res = await apiClient.post<JobAccepted>("/api/content/items/generate", payload);
  return res.data;
}

export async function generateImagePrompt(payload: GenerateImagePromptPayload): Promise<JobAccepted> {
  const res = await apiClient.post<JobAccepted>("/api/content/image-prompts", payload);
  return res.data;
}

export async function getContentQueue(params: ContentQueueParams = {}): Promise<ContentQueueResponse> {
  const res = await apiClient.get<ContentQueueResponse>("/api/content/queue", { params: cleanParams(params) });
  return res.data;
}

export async function getContentItem(id: string): Promise<ContentItem> {
  const res = await apiClient.get<ContentItem>(`/api/content/items/${id}`);
  return res.data;
}

export async function updateContentItem(id: string, payload: UpdateContentItemPayload): Promise<ContentItem> {
  const res = await apiClient.put<ContentItem>(`/api/content/items/${id}`, payload);
  return res.data;
}

export async function uploadContentAsset(id: string, file: File): Promise<ContentAssetUploadResponse> {
  const body = new FormData();
  body.append("file", file);
  const res = await apiClient.post<ContentAssetUploadResponse>(`/api/content/items/${id}/assets`, body);
  return res.data;
}

export async function deleteContentItem(id: string): Promise<void> {
  await apiClient.delete(`/api/content/items/${id}`);
}

export async function getContentPublishingPolicy(): Promise<ContentPublishingPolicy> {
  const res = await apiClient.get<ContentPublishingPolicy>("/api/content/settings/publishing-policy");
  return res.data;
}

export async function updateContentPublishingPolicy(
  payload: UpdateContentPublishingPolicyPayload,
): Promise<ContentPublishingPolicy> {
  const res = await apiClient.put<ContentPublishingPolicy>("/api/content/settings/publishing-policy", payload);
  return res.data;
}

export async function approveContentItem(id: string, payload: ApproveContentItemPayload): Promise<ContentItem> {
  const res = await apiClient.post<ContentItem>(`/api/content/items/${id}/approve`, {
    expectedRevision: payload.expectedRevision,
    overrideReason: payload.overrideReason?.trim() || null,
  });
  return res.data;
}

export async function rejectContentItem(id: string, payload: RejectContentItemPayload): Promise<ContentItem> {
  const res = await apiClient.post<ContentItem>(`/api/content/items/${id}/reject`, {
    expectedRevision: payload.expectedRevision,
    reason: payload.reason?.trim() || null,
  });
  return res.data;
}

export async function retryAgentReview(id: string, payload: RetryAgentReviewPayload): Promise<ContentItem> {
  const res = await apiClient.post<ContentItem>(`/api/content/items/${id}/agent-review/retry`, {
    expectedRevision: payload.expectedRevision,
  });
  return res.data;
}

export async function scheduleContentItem(
  id: string,
  payload: ScheduleContentItemPayload,
): Promise<ContentSchedule> {
  const res = await apiClient.post<ContentSchedule>(`/api/content/items/${id}/schedule`, {
    scheduledAt: payload.scheduledAt,
    metaAssetId: payload.metaAssetId ?? null,
    confirmInstagramAccount: payload.confirmInstagramAccount ?? false,
  });
  return res.data;
}

export async function getContentPublishTargets(platform: string): Promise<ContentPublishTargetsResponse> {
  const res = await apiClient.get<ContentPublishTargetsResponse | readonly ContentPublishTarget[]>("/api/content/publish-targets", { params: { platform } });
  if (Array.isArray(res.data)) {
    const headerMode = res.headers["x-clawbot-publish-target-mode"];
    const mode: ContentPublishTargetMode = headerMode === "standalone"
      || headerMode === "invalid"
      || headerMode === "unsupported"
      ? headerMode
      : "linked_meta";
    return { mode, items: res.data };
  }
  return res.data as ContentPublishTargetsResponse;
}

export async function repurposeContentItem(id: string, targetPlatforms: readonly string[]): Promise<JobAccepted> {
  const res = await apiClient.post<JobAccepted>(`/api/content/items/${id}/repurpose`, {
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

// Phase 4.6: privileged durable retry — resets schedule state for the Hangfire publisher (no inline provider).
export async function retryContentSchedule(id: string): Promise<ContentSchedule> {
  const res = await apiClient.post<ContentSchedule>(`/api/content/schedules/${id}/publish/retry`);
  return res.data;
}

export async function reconcileContentSchedule(
  id: string,
  payload: ReconcilePublishPayload,
): Promise<ContentSchedule> {
  const res = await apiClient.post<ContentSchedule>(`/api/content/schedules/${id}/publish/reconcile`, payload);
  return res.data;
}
