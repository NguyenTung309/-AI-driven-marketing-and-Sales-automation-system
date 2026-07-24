import { useMemo, useState } from "react";
import { Card, Button } from "@/shared/ui";
import { formatDateTime } from "./adminHelpers";
import { EmptyState } from "./adminUi";
import type { AuditLog } from "@/shared/api/admin";

interface AdminAuditTabProps {
  readonly auditLogs: readonly AuditLog[];
  readonly action: string;
  readonly resourceType: string;
  readonly onActionChange: (value: string) => void;
  readonly onResourceTypeChange: (value: string) => void;
}

const ACTION_LABELS: Record<string, string> = {
  create: "Tạo mới",
  update: "Cập nhật",
  delete: "Xóa",
  "auth.login": "Đăng nhập",
  "auth.logout": "Đăng xuất",
};

const RESOURCE_LABELS: Record<string, string> = {
  SocialCredential: "Tài khoản kết nối MXH",
  MetaConnection: "Kết nối Meta",
  MetaAsset: "Tài sản Meta",
  MetaOAuthState: "Trạng thái OAuth Meta",
  Inbox: "Hộp thư",
  User: "Người dùng",
  AppUser: "Người dùng",
  Role: "Vai trò",
  ApiKey: "Khóa API",
  ContentItem: "Nội dung",
  ContentSchedule: "Lịch đăng",
  Contact: "Liên hệ",
  Conversation: "Hội thoại",
  Lead: "Lead",
  AgentConfig: "Cấu hình agent",
  LlmConfig: "Cấu hình LLM",
  Tenant: "Đơn vị",
};

function actionLabel(action: string): string {
  return ACTION_LABELS[action] ?? action;
}

function resourceLabel(resourceType: string): string {
  return RESOURCE_LABELS[resourceType] ?? resourceType;
}

interface DiffEntry {
  readonly property: string;
  readonly from?: unknown;
  readonly to?: unknown;
}

/**
 * BE (AuditSaveChangesInterceptor) serializes a Dictionary:
 * - create:  { "Prop": scalar }
 * - update:  { "Prop": { from, to } }
 * - delete:  { "Prop": { from, to: null } }
 * Array shapes are accepted for forward-compat.
 */
function parseDiff(diffJson: string | null): DiffEntry[] {
  if (!diffJson) return [];
  try {
    const parsed = JSON.parse(diffJson) as unknown;
    if (Array.isArray(parsed)) {
      const rows: DiffEntry[] = [];
      for (const item of parsed) {
        if (!item || typeof item !== "object") continue;
        const row = item as Record<string, unknown>;
        const property =
          typeof row.property === "string"
            ? row.property
            : typeof row.Property === "string"
              ? row.Property
              : null;
        if (!property) continue;
        rows.push({
          property,
          from: row.from ?? row.From,
          to: row.to ?? row.To,
        });
      }
      return rows;
    }

    if (!parsed || typeof parsed !== "object") return [];
    const rows: DiffEntry[] = [];
    for (const [property, value] of Object.entries(parsed as Record<string, unknown>)) {
      if (value && typeof value === "object" && !Array.isArray(value)) {
        const bag = value as Record<string, unknown>;
        if ("from" in bag || "to" in bag || "From" in bag || "To" in bag) {
          rows.push({
            property,
            from: bag.from ?? bag.From,
            to: bag.to ?? bag.To,
          });
          continue;
        }
      }
      // create: scalar value is the "to" side
      rows.push({ property, from: undefined, to: value });
    }
    return rows;
  } catch {
    return [];
  }
}

function formatValue(value: unknown): string {
  if (value === null || value === undefined) return "—";
  if (typeof value === "string") return value;
  try {
    return JSON.stringify(value);
  } catch {
    return String(value);
  }
}

export function AdminAuditTab({
  auditLogs,
  action,
  resourceType,
  onActionChange,
  onResourceTypeChange,
}: AdminAuditTabProps) {
  const [selected, setSelected] = useState<AuditLog | null>(null);
  const selectedDiff = useMemo(() => parseDiff(selected?.diffJson ?? null), [selected]);

  const resourceOptions = useMemo(() => {
    const set = new Set(auditLogs.map((l) => l.resourceType).filter(Boolean));
    return [...set].sort((a, b) => a.localeCompare(b));
  }, [auditLogs]);

  return (
    <section className="space-y-gutter">
      <Card className="p-0">
        <div className="flex flex-col gap-3 border-b border-outline p-card-padding md:flex-row md:items-end md:justify-between">
          <div>
            <h2 className="text-headline-sm text-secondary">Nhật ký quản trị</h2>
            <p className="mt-1 text-body-md text-on-surface-variant">
              Thay đổi dữ liệu do người dùng / hệ thống ghi nhận (không bao gồm log lỗi kỹ thuật).
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            <label className="flex flex-col gap-1 text-label-sm text-on-surface-variant">
              Hành động
              <select
                className="min-w-[140px] rounded-md border border-outline bg-white px-3 py-2 text-body-md text-secondary"
                value={action}
                onChange={(e) => onActionChange(e.target.value)}
              >
                <option value="">Tất cả</option>
                <option value="create">Tạo mới</option>
                <option value="update">Cập nhật</option>
                <option value="delete">Xóa</option>
                <option value="auth.login">Đăng nhập</option>
              </select>
            </label>
            <label className="flex flex-col gap-1 text-label-sm text-on-surface-variant">
              Đối tượng
              <select
                className="min-w-[180px] rounded-md border border-outline bg-white px-3 py-2 text-body-md text-secondary"
                value={resourceType}
                onChange={(e) => onResourceTypeChange(e.target.value)}
              >
                <option value="">Tất cả</option>
                {resourceOptions.map((type) => (
                  <option key={type} value={type}>
                    {resourceLabel(type)}
                  </option>
                ))}
              </select>
            </label>
          </div>
        </div>
        <div className="overflow-x-auto">
          <table className="min-w-[1000px] w-full border-collapse text-left">
            <thead className="bg-surface-variant text-label-sm uppercase text-secondary">
              <tr>
                <th className="px-4 py-3 font-bold">Thời điểm</th>
                <th className="px-4 py-3 font-bold">Hành động</th>
                <th className="px-4 py-3 font-bold">Đối tượng</th>
                <th className="px-4 py-3 font-bold">Người thực hiện</th>
                <th className="px-4 py-3 font-bold">IP</th>
                <th className="px-4 py-3 font-bold">Thay đổi</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-outline bg-white">
              {auditLogs.map((log) => (
                <tr key={log.id} className="hover:bg-surface-container-low">
                  <td className="px-4 py-4 text-body-md text-on-surface-variant">{formatDateTime(log.occurredAt)}</td>
                  <td className="px-4 py-4 font-semibold text-secondary">{actionLabel(log.action)}</td>
                  <td className="px-4 py-4 text-body-md text-secondary">
                    {resourceLabel(log.resourceType)}
                    {log.resourceId ? (
                      <span className="ml-2 font-mono text-mono-status text-on-surface-variant">
                        {log.resourceId.slice(0, 8)}
                      </span>
                    ) : null}
                  </td>
                  <td className="px-4 py-4 text-body-md text-on-surface-variant">{log.userEmail ?? "—"}</td>
                  <td className="px-4 py-4 text-body-md text-on-surface-variant">{log.ipAddress ?? "—"}</td>
                  <td className="px-4 py-4 text-body-md text-on-surface-variant">
                    {log.diffJson ? (
                      <button
                        type="button"
                        className="font-semibold text-primary underline-offset-2 hover:underline"
                        onClick={() => setSelected(log)}
                      >
                        Xem thay đổi
                      </button>
                    ) : (
                      "—"
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {!auditLogs.length ? (
          <div className="p-card-padding">
            <EmptyState>Chưa có nhật ký quản trị.</EmptyState>
          </div>
        ) : null}
      </Card>

      {selected ? (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30 p-4" role="dialog" aria-modal="true">
          <div className="max-h-[80vh] w-full max-w-2xl overflow-hidden rounded-lg bg-white shadow-xl">
            <div className="flex items-center justify-between border-b border-outline p-card-padding">
              <div>
                <h3 className="text-headline-sm text-secondary">
                  {actionLabel(selected.action)} · {resourceLabel(selected.resourceType)}
                </h3>
                <p className="mt-1 text-body-md text-on-surface-variant">{formatDateTime(selected.occurredAt)}</p>
              </div>
              <Button type="button" variant="outline" onClick={() => setSelected(null)}>
                Đóng
              </Button>
            </div>
            <div className="max-h-[60vh] overflow-auto p-card-padding">
              {selectedDiff.length ? (
                <table className="w-full border-collapse text-left">
                  <thead className="bg-surface-variant text-label-sm uppercase text-secondary">
                    <tr>
                      <th className="px-3 py-2 font-bold">Trường</th>
                      <th className="px-3 py-2 font-bold">Từ</th>
                      <th className="px-3 py-2 font-bold">Thành</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-outline">
                    {selectedDiff.map((row) => (
                      <tr key={row.property}>
                        <td className="px-3 py-2 font-semibold text-secondary">{row.property}</td>
                        <td className="max-w-[220px] truncate px-3 py-2 text-body-md text-on-surface-variant" title={formatValue(row.from)}>
                          {formatValue(row.from)}
                        </td>
                        <td className="max-w-[220px] truncate px-3 py-2 text-body-md text-secondary" title={formatValue(row.to)}>
                          {formatValue(row.to)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              ) : (
                <pre className="overflow-auto rounded-md bg-surface-container-low p-3 text-mono-status text-secondary">
                  {selected.diffJson}
                </pre>
              )}
            </div>
          </div>
        </div>
      ) : null}
    </section>
  );
}
