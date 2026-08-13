import { AxiosError } from "axios";
import { apiClient } from "./client";
import type { TenantBranding } from "./publicWidget";

export interface PagedResponse<T> {
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
  readonly items: readonly T[];
}

export interface PancakeChannelInfo {
  readonly inboxId: string;
  readonly pageId: string;
  readonly name: string;
  readonly platform: string;
  readonly hasToken: boolean;
}

export interface AdminUser {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  readonly phone: string | null;
  readonly isActive: boolean;
  readonly lastLoginAt: string | null;
  readonly roles: readonly string[] | null;
  readonly pancakeChannels: readonly PancakeChannelInfo[];
}

export interface Role {
  readonly id: string;
  readonly name: string;
  readonly description: string | null;
  readonly isSystem: boolean;
}

export interface Permission {
  readonly id: string;
  readonly code: string;
  readonly description: string | null;
}

export interface ApiKeyItem {
  readonly id: string;
  readonly name: string;
  readonly createdAt: string;
  readonly expiresAt: string | null;
  readonly revokedAt: string | null;
  readonly scopes: readonly string[] | null;
}

export interface CreatedApiKey {
  readonly id: string;
  readonly name: string;
  readonly plaintextKey: string;
  readonly expiresAt: string | null;
}

export interface PancakeConfig {
  readonly id: string;
  readonly baseUrl: string;
  readonly hasAccessToken: boolean;
  readonly hasWebhookSecret: boolean;
  readonly signatureHeader: string;
  readonly signatureAlgo: string;
  readonly signatureEncoding: string;
  readonly sendPathTemplate: string;
  readonly authMode: string;
  readonly isActive: boolean;
  readonly updatedAt: string;
}

export interface PancakeWebhookUrl {
  readonly webhookUrl: string;
  readonly tenantSlug: string;
}

export type TenantBrandingUpdate = Partial<{
  readonly brandName: string | null;
  readonly logoUrl: string | null;
  readonly primaryColor: string;
  readonly accentColor: string;
  readonly supportName: string | null;
  readonly widgetGreeting: string | null;
}>;

export interface AuditLog {
  readonly id: string;
  readonly userId: string | null;
  readonly userEmail: string | null;
  readonly action: string;
  readonly resourceType: string;
  readonly resourceId: string | null;
  readonly diffJson: string | null;
  readonly ipAddress: string | null;
  readonly userAgent: string | null;
  readonly occurredAt: string;
}

export interface SystemLogEntry {
  readonly id: number;
  readonly occurredAt: string;
  readonly level: string;
  readonly source: string;
  readonly category: string | null;
  readonly message: string;
  readonly statusCode: number | null;
  readonly method: string | null;
  readonly path: string | null;
  readonly elapsedMs: number | null;
  readonly traceId: string | null;
  readonly userId: string | null;
}

export interface SystemLogDetail extends SystemLogEntry {
  readonly exception: string | null;
  readonly tenantId: string | null;
  readonly properties: string | null;
}

export interface SystemLogSummary {
  readonly errors24h: number;
  readonly warnings24h: number;
}

export interface SystemLogCursorPage {
  readonly items: readonly SystemLogEntry[];
  readonly nextCursor: string | null;
  readonly total: number | null;
  readonly summary: SystemLogSummary;
}

export interface ListUsersParams {
  readonly q?: string;
  readonly page?: number;
  readonly pageSize?: number;
}

export async function listAdminUsers(params?: ListUsersParams): Promise<PagedResponse<AdminUser>> {
  const res = await apiClient.get<PagedResponse<AdminUser>>("/api/admin/users", { params });
  return res.data;
}

export async function createAdminUser(body: {
  readonly email: string;
  readonly displayName: string;
  readonly password: string;
  readonly roles?: readonly string[];
  readonly pancakePageId?: string;
  readonly pancakeChannelName?: string;
  readonly pancakePlatform?: string;
  readonly pancakeAccessToken?: string;
}): Promise<Pick<AdminUser, "id" | "email" | "displayName">> {
  const res = await apiClient.post<Pick<AdminUser, "id" | "email" | "displayName">>("/api/admin/users", body);
  return res.data;
}

export async function updateAdminUser(
  id: string,
  body: {
    readonly displayName?: string;
    readonly isActive?: boolean;
    readonly roles?: readonly string[];
    readonly pancakePageId?: string;
    readonly pancakeChannelName?: string;
    readonly pancakePlatform?: string;
    readonly pancakeAccessToken?: string;
  }
): Promise<void> {
  await apiClient.put(`/api/admin/users/${id}`, body);
}

export async function setAdminUserActive(id: string, active: boolean): Promise<{ readonly id: string; readonly isActive: boolean }> {
  const action = active ? "enable" : "disable";
  const res = await apiClient.post<{ readonly id: string; readonly isActive: boolean }>(`/api/admin/users/${id}/${action}`);
  return res.data;
}

export async function resetAdminUserPassword(id: string): Promise<{ readonly message: string }> {
  const res = await apiClient.post<{ readonly message: string }>(`/api/admin/users/${id}/reset-password`);
  return res.data;
}

export async function listRoles(): Promise<readonly Role[]> {
  const res = await apiClient.get<readonly Role[]>("/api/rbac/roles");
  return res.data;
}

export async function createRole(body: { readonly name: string; readonly description?: string | null }): Promise<Role> {
  const res = await apiClient.post<Role>("/api/rbac/roles", body);
  return res.data;
}

export async function updateRole(id: string, body: { readonly name: string; readonly description?: string | null }): Promise<Role> {
  const res = await apiClient.put<Role>(`/api/rbac/roles/${id}`, body);
  return res.data;
}

export async function deleteRole(id: string): Promise<void> {
  await apiClient.delete(`/api/rbac/roles/${id}`);
}

export async function listPermissions(): Promise<readonly Permission[]> {
  const res = await apiClient.get<readonly Permission[]>("/api/rbac/permissions");
  return res.data;
}

export async function listRolePermissions(id: string): Promise<readonly Permission[]> {
  const res = await apiClient.get<readonly Permission[]>(`/api/rbac/roles/${id}/permissions`);
  return res.data;
}

export async function setRolePermissions(id: string, permissionIds: readonly string[]): Promise<void> {
  await apiClient.put(`/api/rbac/roles/${id}/permissions`, { permissionIds });
}

export async function listApiKeys(): Promise<readonly ApiKeyItem[]> {
  const res = await apiClient.get<readonly ApiKeyItem[]>("/api/api-keys");
  return res.data;
}

export async function createApiKey(body: {
  readonly name: string;
  readonly scopes: readonly string[];
  readonly expiresAt?: string | null;
}): Promise<CreatedApiKey> {
  const res = await apiClient.post<CreatedApiKey>("/api/api-keys", body);
  return res.data;
}

export async function revokeApiKey(id: string): Promise<void> {
  await apiClient.delete(`/api/api-keys/${id}`);
}

export async function getPancakeConfig(): Promise<PancakeConfig | null> {
  try {
    const res = await apiClient.get<PancakeConfig>("/api/channels/pancake/config");
    return res.data;
  } catch (error) {
    if (error instanceof AxiosError && error.response?.status === 404) return null;
    throw error;
  }
}

export async function updatePancakeConfig(body: Partial<{
  readonly baseUrl: string;
  readonly accessToken: string;
  readonly webhookSecret: string;
  readonly signatureHeader: string;
  readonly signatureAlgo: string;
  readonly signatureEncoding: string;
  readonly sendPathTemplate: string;
  readonly authMode: string;
  readonly isActive: boolean;
}>): Promise<PancakeConfig> {
  const res = await apiClient.put<PancakeConfig>("/api/channels/pancake/config", body);
  return res.data;
}

export async function deletePancakeConfig(): Promise<void> {
  await apiClient.delete("/api/channels/pancake/config");
}

export async function getPancakeWebhookUrl(): Promise<PancakeWebhookUrl> {
  const res = await apiClient.get<PancakeWebhookUrl>("/api/channels/pancake/webhook-url");
  return res.data;
}

export async function getTenantBranding(): Promise<TenantBranding> {
  const res = await apiClient.get<TenantBranding>("/api/admin/tenant/branding");
  return res.data;
}

export async function updateTenantBranding(body: TenantBrandingUpdate): Promise<TenantBranding> {
  const res = await apiClient.put<TenantBranding>("/api/admin/tenant/branding", body);
  return res.data;
}

// Tenant orchestration autonomy: when requireApproval is true, high-risk agent tools (publish, ad spend,
// customer messages) pause for a human instead of auto-executing. Default false = full auto-publish.
export interface TenantOrchestrationSettings {
  readonly requireApproval: boolean;
  /** Hạn mức chi tiêu LLM/tháng (USD); null = dùng mặc định hệ thống ($200). */
  readonly monthlyCostCapUsd: number | null;
  /** Review-gate: bài đăng phải có chữ ký reviewer agent trước khi publish. */
  readonly requireContentReview: boolean;
  /** Manual-mode: mọi AI reply hold chờ người duyệt thay vì gửi tự động. */
  readonly requireChatReplyApproval: boolean;
  /** AI tự học: bật = tri thức chưng cất luôn chờ người duyệt (tắt tự duyệt). */
  readonly requireKbHumanReview: boolean;
  /** Sale gửi tay -> AI nhường bao lâu (phút) rồi tự bật lại và trả lời tin treo. */
  readonly aiAutoReplyResumeMinutes: number;
  /** Bypass review-gate: bật = AI reply gửi thẳng, không qua critic chấm giá/cam kết. */
  readonly skipChatReplyReview: boolean;
  /** Hội thoại chờ quá ngưỡng này (phút) thì cảnh báo sale; quá gấp đôi thì báo Trưởng phòng KD. */
  readonly idleAlertMinutes: number;
  /** Lead im lặng quá N ngày → lost; 0 = tắt. */
  readonly leadLostAfterDays: number;
  /** Một bước điều phối lỗi thì làm gì: pause (chờ người sửa) | replan (AI lập lại) | fail (dừng hẳn). */
  readonly orchestratorFailurePolicy: OrchestratorFailurePolicy;
}

/** Mặc định "pause": dừng tại bước lỗi để người sửa, thay vì để orchestrator lập lại kế hoạch (tốn LLM). */
export type OrchestratorFailurePolicy = "pause" | "replan" | "fail";

export interface TenantOrchestrationUpdateResult {
  readonly requireOrchestrationApproval: boolean;
  readonly monthlyCostCapUsd: number | null;
  readonly requireContentReview: boolean;
  readonly requireChatReplyApproval: boolean;
  readonly requireKbHumanReview: boolean;
  readonly aiAutoReplyResumeMinutes: number;
  readonly skipChatReplyReview: boolean;
  readonly idleAlertMinutes: number;
  readonly leadLostAfterDays: number;
  readonly orchestratorFailurePolicy: OrchestratorFailurePolicy;
}

/** PATCH-like: chỉ field được set mới gửi (undefined = BE giữ nguyên). Tránh GET fail fallback ghi đè. */
export type TenantOrchestrationPatch = {
  readonly requireApproval?: boolean;
  readonly monthlyCostCapUsd?: number | null;
  readonly clearMonthlyCostCapUsd?: boolean;
  readonly requireContentReview?: boolean;
  readonly requireChatReplyApproval?: boolean;
  readonly requireKbHumanReview?: boolean;
  readonly aiAutoReplyResumeMinutes?: number;
  readonly skipChatReplyReview?: boolean;
  readonly idleAlertMinutes?: number;
  readonly leadLostAfterDays?: number;
  readonly orchestratorFailurePolicy?: OrchestratorFailurePolicy;
};

export async function getTenantOrchestration(): Promise<TenantOrchestrationSettings> {
  const res = await apiClient.get<TenantOrchestrationSettings>("/api/admin/tenant/orchestration");
  return res.data;
}

export async function setTenantOrchestration(
  patch: TenantOrchestrationPatch,
): Promise<TenantOrchestrationUpdateResult> {
  const res = await apiClient.put<TenantOrchestrationUpdateResult>(
    "/api/admin/tenant/orchestration",
    {
      requireApproval: patch.requireApproval,
      monthlyCostCapUsd: patch.monthlyCostCapUsd,
      clearMonthlyCostCapUsd: patch.clearMonthlyCostCapUsd ?? false,
      requireContentReview: patch.requireContentReview,
      requireChatReplyApproval: patch.requireChatReplyApproval,
      requireKbHumanReview: patch.requireKbHumanReview,
      aiAutoReplyResumeMinutes: patch.aiAutoReplyResumeMinutes,
      skipChatReplyReview: patch.skipChatReplyReview,
      idleAlertMinutes: patch.idleAlertMinutes,
      leadLostAfterDays: patch.leadLostAfterDays,
      orchestratorFailurePolicy: patch.orchestratorFailurePolicy,
    },
  );
  return res.data;
}

export async function listAuditLogs(params?: {
  readonly action?: string;
  readonly resourceType?: string;
  readonly page?: number;
  readonly pageSize?: number;
}): Promise<PagedResponse<AuditLog>> {
  const res = await apiClient.get<PagedResponse<AuditLog>>("/api/admin/audit-logs", { params });
  return res.data;
}

export async function listSystemLogs(params?: {
  readonly level?: string;
  readonly statusGroup?: string;
  readonly source?: string;
  readonly q?: string;
  readonly from?: string;
  readonly to?: string;
  readonly cursor?: string | null;
  readonly pageSize?: number;
}): Promise<SystemLogCursorPage> {
  const res = await apiClient.get<SystemLogCursorPage>("/api/admin/system-logs", { params });
  return res.data;
}

export async function getSystemLog(id: number): Promise<SystemLogDetail> {
  const res = await apiClient.get<SystemLogDetail>(`/api/admin/system-logs/${id}`);
  return res.data;
}

export interface RequestStatsPoint {
  readonly bucketHour: string;
  readonly ok2xx: number;
  readonly client4xx: number;
  readonly server5xx: number;
}

export async function listRequestStatsHourly(params?: {
  readonly hours?: number;
}): Promise<readonly RequestStatsPoint[]> {
  const res = await apiClient.get<{ items: readonly RequestStatsPoint[] }>(
    "/api/admin/system-logs/stats/hourly",
    { params },
  );
  return res.data.items;
}

// --- Channel Management ---

export interface SimpleUser {
  readonly id: string;
  readonly displayName: string;
  readonly email: string;
}

export interface InboxItem {
  readonly id: string;
  readonly name: string;
  readonly platform: string;
  readonly externalPageId: string;
  readonly isActive: boolean;
  readonly createdAt: string;
  readonly memberCount: number;
  readonly hasToken: boolean;
}

export async function getSimpleUserList(): Promise<readonly SimpleUser[]> {
  const res = await apiClient.get<readonly SimpleUser[]>("/api/admin/users/simple");
  return res.data;
}

export async function listInboxes(): Promise<readonly InboxItem[]> {
  const res = await apiClient.get<readonly InboxItem[]>("/api/admin/inboxes");
  return res.data;
}

export async function getInboxMembers(inboxId: string): Promise<readonly string[]> {
  const res = await apiClient.get<readonly string[]>(`/api/admin/inboxes/${inboxId}/members`);
  return res.data;
}

export async function updateInboxMember(inboxId: string, agentId: string | null): Promise<void> {
  await apiClient.put(`/api/admin/inboxes/${inboxId}/members`, { agentId });
}

export interface UpdatePancakeChannelRequest {
  readonly name?: string | null;
  readonly pageAccessToken?: string | null;
}

export async function updatePancakeChannel(inboxId: string, body: UpdatePancakeChannelRequest): Promise<void> {
  await apiClient.patch(`/api/admin/pancake-channels/${inboxId}`, body);
}

export async function unlinkInboxMember(inboxId: string, agentId: string): Promise<void> {
  await apiClient.delete(`/api/admin/inboxes/${inboxId}/members/${agentId}`);
}

export async function updateInbox(inboxId: string, pageAccessToken: string): Promise<void> {
  await apiClient.put(`/api/admin/inboxes/${inboxId}`, { pageAccessToken });
}

export interface CreateInboxRequest {
  readonly name: string;
  readonly platform: string;
  readonly externalPageId: string;
  readonly pageAccessToken?: string | null;
  readonly agentId?: string | null;
}

export async function createInbox(body: CreateInboxRequest): Promise<void> {
  await apiClient.post("/api/admin/inboxes", body);
}

// SPEC-16 Module M-5: Pancake channel connect — list pages from a user token, then mint+store page tokens.

export interface PancakePageSummary {
  readonly pageId: string;
  readonly name: string;
  readonly platform: string;
}

export interface ConnectedPancakePage {
  readonly pageId: string;
  readonly name: string;
  readonly platform: string;
  readonly status: string;
  readonly mintedAt?: string | null;
}

/** Validate a Pancake user access token by listing its pages (M-3). Returns the page summaries for selection. */
export async function connectPancake(userAccessToken: string): Promise<readonly PancakePageSummary[]> {
  const res = await apiClient.post<{ items: readonly PancakePageSummary[] }>(
    "/api/admin/channels/pancake/connect",
    { userAccessToken },
  );
  return res.data.items;
}

/** Mint + store a page access token per selected page (M-4). */
export async function mintPancakePages(
  userAccessToken: string,
  pages: readonly PancakePageSummary[],
): Promise<readonly { pageId: string; status: string; error?: string }[]> {
  const res = await apiClient.post<{ items: readonly { pageId: string; status: string; error?: string }[] }>(
    "/api/admin/channels/pancake/pages",
    { userAccessToken, pages: pages.map((p) => ({ pageId: p.pageId, name: p.name, platform: p.platform })) },
  );
  return res.data.items;
}

/** List currently connected Pancake pages with their status (M-5 status view). Never returns the token. */
export async function listConnectedPancakePages(): Promise<readonly ConnectedPancakePage[]> {
  const res = await apiClient.get<{ items: readonly ConnectedPancakePage[] }>(
    "/api/admin/channels/pancake/pages",
  );
  return res.data.items;
}

export interface AdminRecurringJob {
  readonly id: string;
  readonly cron: string;
  readonly queue: string;
  readonly nextExecution?: string | null;
  readonly lastExecution?: string | null;
  readonly lastState?: string | null;
  readonly agent?: string | null;
  readonly description?: string | null;
}

export interface AdminScheduleJob {
  readonly id: string;
  readonly name: string;
  readonly goalTemplate: string;
  readonly cadence: string;
  readonly timezoneId: string;
  readonly nextRunAt: string;
  readonly lastRunAt?: string | null;
  readonly isActive: boolean;
  readonly requiresApproval: boolean;
  readonly agent?: string | null;
  readonly kind: string;
}

export interface AdminJobsResponse {
  readonly recurring: readonly AdminRecurringJob[];
  readonly schedules: readonly AdminScheduleJob[];
}

export async function getAdminJobs(): Promise<AdminJobsResponse> {
  const res = await apiClient.get<AdminJobsResponse>("/api/admin/jobs");
  return res.data;
}

export async function triggerAdminRecurringJob(id: string): Promise<void> {
  await apiClient.post(`/api/admin/jobs/recurring/${encodeURIComponent(id)}/trigger`);
}

export async function pauseAdminScheduleJob(id: string): Promise<void> {
  await apiClient.post(`/api/admin/jobs/schedules/${encodeURIComponent(id)}/pause`);
}

export async function activateAdminScheduleJob(id: string): Promise<void> {
  await apiClient.post(`/api/admin/jobs/schedules/${encodeURIComponent(id)}/activate`);
}

export async function runAdminScheduleJobNow(id: string): Promise<void> {
  await apiClient.post(`/api/admin/jobs/schedules/${encodeURIComponent(id)}/run-now`);
}

export type SocialChannelProvider = "zalo" | "instagram";
export type SocialCredentialResolutionState = "absent" | "disabled" | "resolved" | "invalid";

export interface SocialChannelCredential {
  readonly provider: SocialChannelProvider;
  readonly resolutionState: SocialCredentialResolutionState;
  readonly enabled: boolean;
  readonly endpoint: string;
  readonly pageId: string;
  readonly oaId: string;
  readonly hasPageAccessToken: boolean;
  readonly hasOaAccessToken: boolean;
  readonly updatedAt?: string | null;
}

/** Secret semantics: null = giữ giá trị đã lưu, chuỗi rỗng = xoá. */
export interface UpdateSocialChannelPayload {
  readonly enabled?: boolean | null;
  readonly endpoint?: string | null;
  readonly pageId?: string | null;
  readonly pageAccessToken?: string | null;
  readonly oaId?: string | null;
  readonly oaAccessToken?: string | null;
}

export interface UpdateInstagramCredentialPayload {
  readonly enabled: boolean;
  readonly pageId: string;
  readonly pageAccessToken: string | null;
}

export async function getSocialCredentials(): Promise<readonly SocialChannelCredential[]> {
  const res = await apiClient.get<{ items: readonly SocialChannelCredential[] }>("/api/admin/social-credentials");
  return res.data.items;
}

export async function updateSocialCredential(
  provider: SocialChannelProvider,
  payload: UpdateSocialChannelPayload,
): Promise<SocialChannelCredential> {
  const res = await apiClient.put<SocialChannelCredential>(
    `/api/admin/social-credentials/${encodeURIComponent(provider)}`,
    payload,
  );
  return res.data;
}

export async function updateInstagramCredential(
  payload: UpdateInstagramCredentialPayload,
): Promise<SocialChannelCredential> {
  return updateSocialCredential("instagram", payload);
}

export interface MetaAsset {
  readonly id: string;
  readonly assetType: string;
  readonly externalId: string;
  readonly name: string;
  readonly tasks: readonly string[];
  readonly isDefault: boolean;
  readonly isActive: boolean;
  readonly lastSyncedAt: string;
  readonly canModerateComments: boolean;
  readonly canSendPrivateReplies: boolean;
  readonly feedSubscribedAt?: string | null;
}

export interface MetaEngagementCapability {
  readonly missingCommentScopes: readonly string[];
  readonly missingPrivateReplyScopes: readonly string[];
  readonly activePageCount: number;
  readonly commentCapablePageCount: number;
  readonly privateReplyCapablePageCount: number;
  readonly feedSubscribedPageCount: number;
}

export type MetaAuthorizationMode = "development_user" | "business_system_user";

export interface MetaAppConfiguration {
  readonly configured: boolean;
  readonly businessWebhookConfigured: boolean;
  readonly source: "database" | "environment";
  readonly appId: string;
  readonly configurationId: string;
  readonly authorizationMode: MetaAuthorizationMode;
  readonly hasAppSecret: boolean;
  readonly hasWebhookVerifyToken: boolean;
  readonly redirectUri: string;
  readonly frontendReturnUrl: string;
  readonly apiVersion: string;
  readonly updatedAt?: string | null;
}

export interface MetaIntegrationStatus {
  readonly configured: boolean;
  readonly businessWebhookConfigured: boolean;
  readonly appConfiguration: MetaAppConfiguration;
  readonly connected: boolean;
  readonly status: string;
  readonly clientBusinessId: string;
  readonly systemUserId: string;
  readonly tokenType: "user" | "business_integration_system_user" | "";
  readonly grantedScopes: readonly string[];
  readonly expiresAt?: string | null;
  readonly dataAccessExpiresAt?: string | null;
  readonly lastValidatedAt?: string | null;
  readonly lastError?: string | null;
  readonly assets: readonly MetaAsset[];
  readonly engagement?: MetaEngagementCapability | null;
}

export async function getMetaIntegrationStatus(): Promise<MetaIntegrationStatus> {
  const res = await apiClient.get<MetaIntegrationStatus>("/api/admin/meta");
  return res.data;
}

export async function updateMetaAppConfiguration(body: {
  readonly appId: string;
  readonly appSecret: string | null;
  readonly configurationId: string;
  readonly authorizationMode: MetaAuthorizationMode;
  readonly webhookVerifyToken: string | null;
  readonly redirectUri: string;
  readonly frontendReturnUrl: string;
}): Promise<MetaAppConfiguration> {
  const res = await apiClient.put<MetaAppConfiguration>("/api/admin/meta/config", body);
  return res.data;
}

export async function startMetaConnection(): Promise<{ readonly authorizationUrl: string; readonly expiresAt: string }> {
  const res = await apiClient.post<{ readonly authorizationUrl: string; readonly expiresAt: string }>("/api/admin/meta/connect");
  return res.data;
}

export async function syncMetaAssets(): Promise<MetaIntegrationStatus> {
  const res = await apiClient.post<MetaIntegrationStatus>("/api/admin/meta/sync");
  return res.data;
}

export async function validateMetaConnection(): Promise<MetaIntegrationStatus> {
  const res = await apiClient.post<MetaIntegrationStatus>("/api/admin/meta/validate");
  return res.data;
}

export async function setDefaultMetaAsset(assetId: string): Promise<MetaIntegrationStatus> {
  const res = await apiClient.put<MetaIntegrationStatus>(`/api/admin/meta/assets/${encodeURIComponent(assetId)}/default`);
  return res.data;
}

export async function disconnectMeta(): Promise<void> {
  await apiClient.delete("/api/admin/meta");
}
