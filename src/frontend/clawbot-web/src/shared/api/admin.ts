import { AxiosError } from "axios";
import { apiClient } from "./client";
import type { TenantBranding } from "./publicWidget";

export interface PagedResponse<T> {
  readonly total: number;
  readonly page: number;
  readonly pageSize: number;
  readonly items: readonly T[];
}

export interface AdminUser {
  readonly id: string;
  readonly email: string;
  readonly displayName: string;
  readonly phone: string | null;
  readonly isActive: boolean;
  readonly lastLoginAt: string | null;
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
  readonly action: string;
  readonly resourceType: string;
  readonly resourceId: string | null;
  readonly diffJson: string | null;
  readonly ipAddress: string | null;
  readonly userAgent: string | null;
  readonly occurredAt: string;
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
export async function getTenantOrchestration(): Promise<{ readonly requireApproval: boolean }> {
  const res = await apiClient.get<{ requireApproval: boolean }>("/api/admin/tenant/orchestration");
  return res.data;
}

export async function setTenantOrchestration(requireApproval: boolean): Promise<{ readonly requireOrchestrationApproval: boolean }> {
  const res = await apiClient.put<{ requireOrchestrationApproval: boolean }>("/api/admin/tenant/orchestration", { requireApproval });
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

export async function createInbox(body: CreateInboxRequest): Promise<any> {
  const res = await apiClient.post("/api/admin/inboxes", body);
  return res.data;
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
