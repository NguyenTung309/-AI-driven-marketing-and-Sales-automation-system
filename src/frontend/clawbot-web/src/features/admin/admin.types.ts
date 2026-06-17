import { AxiosError } from "axios";
import type { StatusTone } from "@/shared/ui";
import type {
  AdminUser,
  ApiKeyItem,
  AuditLog,
  CreatedApiKey,
  PancakeConfig,
  PancakeWebhookUrl,
  Permission,
  Role,
} from "@/shared/api/admin";

export type { AdminUser, ApiKeyItem, AuditLog, CreatedApiKey, PancakeConfig, PancakeWebhookUrl, Permission, Role };

export type AdminTab = "users" | "roles" | "keys" | "integrations" | "audit";
export type UserModalMode = "create" | "edit" | null;
export type RoleModalMode = "create" | "edit" | null;

export const EMPTY_USERS: readonly AdminUser[] = [];
export const EMPTY_ROLES: readonly Role[] = [];
export const EMPTY_PERMISSIONS: readonly Permission[] = [];
export const EMPTY_KEYS: readonly ApiKeyItem[] = [];
export const EMPTY_AUDIT_LOGS: readonly AuditLog[] = [];

export const DEFAULT_PANCAKE_FORM = {
  baseUrl: "https://pancake.vn",
  accessToken: "",
  webhookSecret: "",
  signatureHeader: "X-Pancake-Signature",
  signatureAlgo: "HMACSHA256",
  signatureEncoding: "hex",
  sendPathTemplate: "/api/v1/conversations/{conversationId}/messages",
  authMode: "bearer",
  isActive: false,
} as const;

export function formatDateTime(value: string | null | undefined): string {
  if (!value) return "—";
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

export function formatDate(value: string | null | undefined): string {
  if (!value) return "Không xuất hiện";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("vi-VN", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  }).format(date);
}

export function errorMessage(error: unknown): string {
  if (!error) return "";
  if (error instanceof AxiosError) {
    const data = error.response?.data as
      | { error?: string; title?: string; detail?: string; message?: string }
      | string[]
      | string
      | undefined;
    if (Array.isArray(data)) return data.join(", ");
    if (typeof data === "string") return data;
    return data?.message ?? data?.error ?? data?.title ?? data?.detail ?? error.message;
  }
  if (error instanceof Error) return error.message;
  return "Không xử lý được yêu cầu. Vui lòng thử lại.";
}

export function parseScopes(value: string): readonly string[] {
  return value
    .split(/[\n,]/)
    .map((item) => item.trim())
    .filter(Boolean);
}

export function roleTone(role: Role): StatusTone {
  return role.isSystem ? "warning" : "neutral";
}

export function keyTone(key: ApiKeyItem): StatusTone {
  if (key.revokedAt) return "error";
  if (key.expiresAt && new Date(key.expiresAt).getTime() < Date.now()) return "warning";
  return "success";
}

export function keyStatus(key: ApiKeyItem): string {
  if (key.revokedAt) return "Đã thu hồi";
  if (key.expiresAt && new Date(key.expiresAt).getTime() < Date.now()) return "Hết hạn";
  return "Đang hoạt động";
}

export const inputClass =
  "w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary";