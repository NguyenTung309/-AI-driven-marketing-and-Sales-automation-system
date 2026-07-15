import { Alert, Button, Card, StatusPill } from "@/shared/ui";
import { formatDate, formatDateTime, keyStatus, keyTone } from "./adminHelpers";
import { EmptyState } from "./adminUi";
import type { ApiKeyItem, CreatedApiKey } from "@/shared/api/admin";

interface AdminKeysTabProps {
  readonly apiKeys: readonly ApiKeyItem[];
  readonly createdKey: CreatedApiKey | null;
  readonly onOpenCreateKey: () => void;
  readonly onRevokeKey: (key: ApiKeyItem) => void;
  readonly revokeKeyPending: boolean;
}

export function AdminKeysTab({ apiKeys, createdKey, onOpenCreateKey, onRevokeKey, revokeKeyPending }: AdminKeysTabProps) {
  return (
    <section className="space-y-gutter">
      <Card className="p-0">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-outline p-card-padding">
          <div>
            <h2 className="text-headline-sm text-secondary">Khóa tích hợp</h2>
            <p className="mt-1 text-body-md text-on-surface-variant">Khóa tích hợp giữa các hệ thống. Mã bí mật chỉ hiển thị một lần khi phát hành.</p>
          </div>
          <Button type="button" onClick={onOpenCreateKey}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">add</span>
            Phát hành khóa
          </Button>
        </div>
        {createdKey ? (
          <div className="border-b border-outline p-card-padding">
            <Alert tone="warning">
              Khóa tích hợp mới: <span className="font-mono">{createdKey.plaintextKey}</span>
            </Alert>
          </div>
        ) : null}
        <div className="overflow-x-auto">
          <table className="min-w-[820px] w-full border-collapse text-left">
            <thead className="bg-surface-variant text-label-sm uppercase text-secondary">
              <tr>
                <th className="px-4 py-3 font-bold">Tên khóa</th>
                <th className="px-4 py-3 font-bold">Quyền truy cập</th>
                <th className="px-4 py-3 font-bold">Ngày tạo</th>
                <th className="px-4 py-3 font-bold">Hết hạn</th>
                <th className="px-4 py-3 font-bold">Trạng thái</th>
                <th className="px-4 py-3 text-right font-bold">Hành động</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-outline bg-white">
              {apiKeys.map((key) => (
                <tr key={key.id} className="hover:bg-surface-container-low">
                  <td className="px-4 py-4 font-semibold text-secondary">{key.name}</td>
                  <td className="px-4 py-4">
                    <div className="flex max-w-[320px] flex-wrap gap-1">
                      {(key.scopes ?? []).length ? (
                        key.scopes?.map((scope) => <StatusPill key={scope} tone="neutral">Quyền tích hợp</StatusPill>)
                      ) : (
                        <span className="text-body-md text-on-surface-variant">Không giới hạn quyền</span>
                      )}
                    </div>
                  </td>
                  <td className="px-4 py-4 text-body-md text-on-surface-variant">{formatDateTime(key.createdAt)}</td>
                  <td className="px-4 py-4 text-body-md text-on-surface-variant">{formatDate(key.expiresAt)}</td>
                  <td className="px-4 py-4"><StatusPill tone={keyTone(key)}>{keyStatus(key)}</StatusPill></td>
                  <td className="px-4 py-4 text-right">
                    <Button
                      type="button"
                      size="sm"
                      variant="ghost"
                      onClick={() => onRevokeKey(key)}
                      disabled={Boolean(key.revokedAt) || revokeKeyPending}
                    >
                      <span aria-hidden="true" className="material-symbols-outlined text-[18px]">block</span>
                      Thu hồi
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {!apiKeys.length ? <div className="p-card-padding"><EmptyState>Chưa phát hành khóa tích hợp.</EmptyState></div> : null}
      </Card>
    </section>
  );
}
