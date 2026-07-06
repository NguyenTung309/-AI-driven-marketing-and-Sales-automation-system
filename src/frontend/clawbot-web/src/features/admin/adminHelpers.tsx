import { isAxiosError } from "axios";
import { Card, StatusPill, type StatusTone } from "@/shared/ui";
import { toUserFriendlyError } from "@/shared/utils/userText";
import type { ApiKeyItem, Role } from "@/shared/api/admin";

export type ConfirmTarget =
  | { readonly kind: "resetPassword"; readonly id: string; readonly label: string }
  | { readonly kind: "deleteRole"; readonly id: string; readonly label: string }
  | { readonly kind: "revokeKey"; readonly id: string; readonly label: string }
  | { readonly kind: "deletePancake" };

export function confirmCopy(target: ConfirmTarget): { readonly title: string; readonly message: string; readonly confirmLabel: string } {
  switch (target.kind) {
    case "resetPassword":
      return { title: "Đặt lại mật khẩu?", message: `Hệ thống sẽ phát hành mã đặt lại mật khẩu cho ${target.label}.`, confirmLabel: "Đặt lại" };
    case "deleteRole":
      return { title: "Xóa vai trò?", message: `Vai trò "${target.label}" sẽ bị xóa vĩnh viễn.`, confirmLabel: "Xóa" };
    case "revokeKey":
      return { title: "Thu hồi khóa tích hợp?", message: `Khóa "${target.label}" sẽ ngừng hoạt động ngay lập tức.`, confirmLabel: "Thu hồi" };
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

export const DEFAULT_BRANDING_FORM = {
  brandName: "",
  logoUrl: "",
  primaryColor: "#d32f2f",
  accentColor: "#f59e0b",
  supportName: "",
  widgetGreeting: "",
};
export type BrandingForm = typeof DEFAULT_BRANDING_FORM;

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

export function MetricTile({
  icon,
  label,
  value,
  tone = "neutral",
}: {
  readonly icon: string;
  readonly label: string;
  readonly value: string;
  readonly tone?: StatusTone;
}) {
  return (
    <Card>
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-label-caps uppercase text-on-surface-variant">{label}</p>
          <p className="mt-2 text-telemetry-data text-secondary">{value}</p>
        </div>
        <span aria-hidden="true" className="material-symbols-outlined rounded bg-primary/10 p-2 text-primary">{icon}</span>
      </div>
      <div className="mt-3">
        <StatusPill tone={tone}>Quản trị</StatusPill>
      </div>
    </Card>
  );
}

export function EmptyState({ children }: { readonly children: string }) {
  return (
    <div className="rounded-lg border border-dashed border-outline bg-surface p-6 text-center text-body-md text-on-surface-variant">
      {children}
    </div>
  );
}

export function TabButton({
  active,
  icon,
  label,
  onClick,
}: {
  readonly active: boolean;
  readonly icon: string;
  readonly label: string;
  readonly onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`inline-flex items-center gap-2 border-b-2 px-4 py-3 text-label-caps uppercase ${
        active ? "border-primary text-primary" : "border-transparent text-on-surface-variant hover:text-secondary"
      }`}
    >
      <span aria-hidden="true" className="material-symbols-outlined text-[18px]">{icon}</span>
      {label}
    </button>
  );
}

export function Field({
  label,
  children,
}: {
  readonly label: string;
  readonly children: React.ReactNode;
}) {
  return (
    <label className="block">
      <span className="mb-1 block text-label-sm font-semibold text-secondary">{label}</span>
      {children}
    </label>
  );
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
