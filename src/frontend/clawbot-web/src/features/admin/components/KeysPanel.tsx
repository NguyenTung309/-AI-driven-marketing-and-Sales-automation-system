import { Button, Card, StatusPill } from "@/shared/ui";
import { formatDate, formatDateTime } from "../admin.types";
import type { ApiKeyItem, CreatedApiKey } from "../admin.types";
import { keyTone, keyStatus } from "../admin.types";
import { EmptyState } from "./EmptyState";

interface KeysPanelProps {
  readonly apiKeys: readonly ApiKeyItem[];
  readonly createdKey: CreatedApiKey | null;
  readonly onRevoke: (id: string) => void;
  readonly onOpenCreate: () => void;
  readonly isRevokePending: boolean;
}

export function KeysPanel({
  apiKeys,
  createdKey,
  onRevoke,
  onOpenCreate,
  isRevokePending,
}: KeysPanelProps) {
  return (
    <section className="space-y-gutter">
      <Card className="p-0">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-outline p-card-padding">
          <div>
            <h2 className="text-headline-sm text-secondary">API keys</h2>
            <p className="mt-1 text-body-md text-on-surface-variant">
              Khóa tích hợp server-to-server. Secret chỉ hiển thị một lần khi phát hành.
            </p>
          </div>
          <Button type="button" onClick={onOpenCreate}>
            <span className="material-symbols-outlined text-[18px]">add</span>
            Phát hành key
          </Button>
        </div>

        {createdKey ? (
          <div className="border-b border-outline p-card-padding">
            <div className="rounded bg-warning/10 p-3 text-body-md text-warning">
              API key mới: <span className="font-mono">{createdKey.plaintextKey}</span>
            </div>
          </div>
        ) : null}

        <div className="overflow-x-auto">
          <table className="min-w-[820px] w-full border-collapse text-left">
            <thead className="bg-surface-variant text-label-sm uppercase text-secondary">
              <tr>
                <th className="px-4 py-3 font-bold">Tên key</th>
                <th className="px-4 py-3 font-bold">Scopes</th>
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
                        key.scopes?.map((scope) => (
                          <StatusPill key={scope} tone="neutral">
                            {scope}
                          </StatusPill>
                        ))
                      ) : (
                        <span className="text-body-md text-on-surface-variant">
                          Không giới hạn scope
                        </span>
                      )}
                    </div>
                  </td>

                  <td className="px-4 py-4 text-body-md text-on-surface-variant">
                    {formatDateTime(key.createdAt)}
                  </td>

                  <td className="px-4 py-4 text-body-md text-on-surface-variant">
                    {formatDate(key.expiresAt)}
                  </td>

                  <td className="px-4 py-4">
                    <StatusPill tone={keyTone(key)}>{keyStatus(key)}</StatusPill>
                  </td>

                  <td className="px-4 py-4 text-right">
                    <Button
                      type="button"
                      size="sm"
                      variant="ghost"
                      onClick={() => onRevoke(key.id)}
                      disabled={Boolean(key.revokedAt) || isRevokePending}
                    >
                      <span className="material-symbols-outlined text-[18px]">block</span>
                      Thu hồi
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {!apiKeys.length ? (
          <div className="p-card-padding">
            <EmptyState>Chưa phát hành API key.</EmptyState>
          </div>
        ) : null}
      </Card>
    </section>
  );
}