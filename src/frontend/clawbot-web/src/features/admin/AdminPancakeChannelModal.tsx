import { useState } from "react";
import { Alert, Button, Modal, StatusPill } from "@/shared/ui";
import type { PancakeChannelInfo, SimpleUser, UpdatePancakeChannelRequest } from "@/shared/api/admin";
import { adminFormErrorMessage, inputClass } from "./adminHelpers";
import { Field } from "./adminUi";

export interface PancakeChannelTarget {
  readonly userId: string;
  readonly userDisplayName: string;
  readonly channel: PancakeChannelInfo;
}

interface AdminPancakeChannelModalProps {
  readonly target: PancakeChannelTarget | null;
  readonly canManagePancakeToken: boolean;
  readonly canManageInboxOwners: boolean;
  readonly ownerOptions: readonly SimpleUser[];
  readonly ownerOptionsLoading: boolean;
  readonly metadataPending: boolean;
  readonly ownerPending: boolean;
  readonly metadataError: unknown;
  readonly ownerError: unknown;
  readonly onSaveMetadata: (body: UpdatePancakeChannelRequest) => void;
  readonly onChangeOwner: (agentId: string) => void;
  readonly onRequestUnlink: () => void;
  readonly onClose: () => void;
}

export function AdminPancakeChannelModal({
  target,
  canManagePancakeToken,
  canManageInboxOwners,
  ownerOptions,
  ownerOptionsLoading,
  metadataPending,
  ownerPending,
  metadataError,
  ownerError,
  onSaveMetadata,
  onChangeOwner,
  onRequestUnlink,
  onClose,
}: AdminPancakeChannelModalProps) {
  const [name, setName] = useState("");
  const [pageAccessToken, setPageAccessToken] = useState("");
  const [selectedOwnerId, setSelectedOwnerId] = useState("");
  const [hydratedTarget, setHydratedTarget] = useState<PancakeChannelTarget | null>(null);

  if (target && target !== hydratedTarget) {
    setHydratedTarget(target);
    setName(target.channel.name === target.channel.pageId ? "" : target.channel.name);
    setPageAccessToken("");
    setSelectedOwnerId(target.userId);
  }

  if (!target) return null;

  const ownerOptionExists = ownerOptions.some((user) => user.id === target.userId);
  const isOwnerChange = selectedOwnerId.length > 0 && selectedOwnerId !== target.userId;
  const formId = `admin-pancake-channel-form-${target.channel.inboxId}`;

  return (
    <Modal
      open
      onClose={onClose}
      title={`Quản lý kênh ${target.channel.name || target.channel.pageId}`}
      footer={
        <Button type="button" variant="ghost" onClick={onClose} disabled={metadataPending || ownerPending}>
          Đóng
        </Button>
      }
    >
      <div className="space-y-5">
        {metadataError ? <Alert tone="error">{adminFormErrorMessage(metadataError)}</Alert> : null}
        {ownerError ? <Alert tone="error">{adminFormErrorMessage(ownerError)}</Alert> : null}

        {canManagePancakeToken ? (
          <form
            id={formId}
            className="space-y-3 rounded-lg border border-outline bg-surface p-4"
            onSubmit={(event) => {
              event.preventDefault();
              const body: UpdatePancakeChannelRequest = {
                name: name.trim(),
                ...(pageAccessToken.trim() ? { pageAccessToken: pageAccessToken.trim() } : {}),
              };
              onSaveMetadata(body);
            }}
          >
            <div className="flex items-start justify-between gap-3">
              <div>
                <h3 className="text-title-md font-semibold text-secondary">Thông tin kênh</h3>
                <p className="mt-1 text-label-sm text-on-surface-variant">Tên và token áp dụng cho kênh này ở mọi nơi.</p>
              </div>
              <StatusPill tone={target.channel.hasToken ? "success" : "warning"}>
                {target.channel.hasToken ? "Có token" : "Thiếu token"}
              </StatusPill>
            </div>
            <Field label="Tên kênh">
              <input className={inputClass} value={name} onChange={(event) => setName(event.target.value)} placeholder={target.channel.pageId} />
            </Field>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <Field label="Page ID">
                <input className={`${inputClass} bg-surface-variant`} value={target.channel.pageId} readOnly />
              </Field>
              <Field label="Nền tảng">
                <input className={`${inputClass} bg-surface-variant`} value={target.channel.platform} readOnly />
              </Field>
            </div>
            <Field label="Token thay thế">
              <input
                className={inputClass}
                type="password"
                value={pageAccessToken}
                onChange={(event) => setPageAccessToken(event.target.value)}
                placeholder={target.channel.hasToken ? "Đã lưu token, nhập để thay thế" : "Nhập page access token Pancake"}
                autoComplete="new-password"
              />
            </Field>
            <div className="flex justify-end">
              <Button type="submit" form={formId} disabled={metadataPending}>
                {metadataPending ? "Đang lưu..." : "Lưu thông tin kênh"}
              </Button>
            </div>
          </form>
        ) : null}

        {canManageInboxOwners ? (
          <section className="space-y-3 rounded-lg border border-outline bg-surface p-4">
            <div>
              <h3 className="text-title-md font-semibold text-secondary">Người phụ trách</h3>
              <p className="mt-1 text-label-sm text-on-surface-variant">
                Đang gán cho {target.userDisplayName}. Đổi người phụ trách sẽ bỏ gán hội thoại của người cũ trong kênh này.
              </p>
            </div>
            <Field label="Nhân viên mới">
              <select className={inputClass} value={selectedOwnerId} onChange={(event) => setSelectedOwnerId(event.target.value)} disabled={ownerOptionsLoading || ownerPending}>
                {!ownerOptionExists ? <option value={target.userId}>{target.userDisplayName} (hiện tại)</option> : null}
                {ownerOptions.map((user) => (
                  <option key={user.id} value={user.id}>
                    {user.displayName} - {user.email}
                  </option>
                ))}
              </select>
            </Field>
            <div className="flex justify-end">
              <Button type="button" variant="outline" onClick={() => onChangeOwner(selectedOwnerId)} disabled={!isOwnerChange || ownerPending || ownerOptionsLoading}>
                {ownerPending ? "Đang đổi..." : "Đổi người phụ trách"}
              </Button>
            </div>
            <div className="border-t border-outline pt-3">
              <p className="text-label-sm text-on-surface-variant">Gỡ nhân viên khỏi kênh. Kênh vẫn được giữ lại và có thể gán lại sau.</p>
              <Button type="button" variant="outline" className="mt-2 border-error text-error hover:bg-error/10" onClick={onRequestUnlink} disabled={ownerPending}>
                Gỡ khỏi kênh
              </Button>
            </div>
          </section>
        ) : null}
      </div>
    </Modal>
  );
}
