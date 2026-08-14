import { isAxiosError } from "axios";
import type { StatusTone } from "@/shared/ui";
import { toUserFriendlyError } from "@/shared/utils/userText";
import type { ApiKeyItem, Role } from "@/shared/api/admin";

export type ConfirmTarget =
  | { readonly kind: "resetPassword"; readonly id: string; readonly label: string }
  | { readonly kind: "deleteRole"; readonly id: string; readonly label: string }
  | { readonly kind: "deletePancake" };

export function confirmCopy(target: ConfirmTarget): { readonly title: string; readonly message: string; readonly confirmLabel: string } {
  switch (target.kind) {
    case "resetPassword":
      return { title: "Đặt lại mật khẩu?", message: `Hệ thống sẽ phát hành mã đặt lại mật khẩu cho ${target.label}.`, confirmLabel: "Đặt lại" };
    case "deleteRole":
      return { title: "Xóa vai trò?", message: `Vai trò "${target.label}" sẽ bị xóa vĩnh viễn.`, confirmLabel: "Xóa" };
    case "deletePancake":
      return { title: "Ngắt kết nối Pancake?", message: "Cấu hình Pancake hiện tại sẽ bị xóa.", confirmLabel: "Ngắt kết nối" };
  }
}

export const DEFAULT_PANCAKE_FORM = {
  baseUrl: "https://pancake.vn",
  accessToken: "",
  webhookSecret: "",
  signatureHeader: "X-Pancake-Signature",
  signatureAlgo: "HMACSHA256",
  signatureEncoding: "hex",
  sendPathTemplate: "",
  authMode: "bearer",
  isActive: false,
};
export type PancakeForm = typeof DEFAULT_PANCAKE_FORM;

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
  if (!value) return "Không giới hạn";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat("vi-VN", { day: "2-digit", month: "2-digit", year: "numeric" }).format(date);
}

export function errorMessage(error: unknown): string {
  return toUserFriendlyError(error, "Không xử lý được yêu cầu quản trị. Vui lòng thử lại.");
}

export function adminFormErrorMessage(error: unknown): string {
  const data = isAxiosError(error) ? error.response?.data : null;
  if (Array.isArray(data) && data.every((item) => typeof item === "string")) return data.join("\n");
  return errorMessage(error);
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

export const inputClass = "w-full rounded border border-outline bg-white px-3 py-2 text-body-md outline-none focus:border-primary";
export const tempPasswordPattern = "(?=.*[a-z])(?=.*[A-Z])(?=.*\\d)(?=.*[^A-Za-z0-9]).{8,}";
export const tempPasswordHint = "Ít nhất 8 ký tự, gồm chữ hoa, chữ thường, số và ký tự đặc biệt.";

export interface AdminUserFormState {
  readonly displayName: string;
  readonly email: string;
  readonly password: string;
  readonly isActive: boolean;
  readonly roles: string[];
  readonly pancakePageId: string;
  readonly pancakeChannelName: string;
  readonly pancakePlatform: string;
  readonly pancakeAccessToken: string;
}

export interface AdminRoleFormState {
  readonly name: string;
  readonly description: string;
}

export interface AdminKeyFormState {
  readonly name: string;
  readonly scopes: string;
  readonly expiresAt: string;
}
