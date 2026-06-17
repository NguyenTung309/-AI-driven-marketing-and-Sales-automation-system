import { Button, Card, StatusPill, type StatusTone } from "@/shared/ui";
import type { PancakeConfig, PancakeWebhookUrl } from "../admin.types";
import { inputClass } from "../admin.types";
import { Field } from "./Field";
import type { UseQueryResult } from "@tanstack/react-query";

interface IntegrationsPanelProps {
  readonly pancakeQuery: UseQueryResult<PancakeConfig | null>;
  readonly webhookQuery: UseQueryResult<PancakeWebhookUrl>;
  readonly pancakeForm: {
    readonly baseUrl: string;
    readonly accessToken: string;
    readonly webhookSecret: string;
    readonly signatureHeader: string;
    readonly signatureAlgo: string;
    readonly signatureEncoding: string;
    readonly sendPathTemplate: string;
    readonly authMode: string;
    readonly isActive: boolean;
  };
  readonly onFormChange: (patch: Record<string, string | boolean>) => void;
  readonly onSave: () => void;
  readonly onDisconnect: () => void;
  readonly onCopyWebhook: () => void;
  readonly isSavePending: boolean;
  readonly isDisconnectPending: boolean;
}

export function IntegrationsPanel({
  pancakeQuery,
  webhookQuery,
  pancakeForm,
  onFormChange,
  onSave,
  onDisconnect,
  onCopyWebhook,
  isSavePending,
  isDisconnectPending,
}: IntegrationsPanelProps) {
  const pancakeActive = pancakeQuery.data?.isActive;
  const statusTone: StatusTone = pancakeActive ? "success" : "warning";
  const statusText = pancakeActive ? "Hoạt động" : "Chưa bật";

  return (
    <section className="grid grid-cols-1 gap-gutter xl:grid-cols-[minmax(0,1fr)_420px]">
      <Card>
        <div className="mb-5 flex flex-wrap items-center justify-between gap-3">
          <div>
            <h2 className="text-headline-sm text-secondary">Kênh Pancake</h2>
            <p className="mt-1 text-body-md text-on-surface-variant">
              Cấu hình gửi/nhận hội thoại qua Pancake và webhook tenant.
            </p>
          </div>
          <StatusPill tone={statusTone}>{statusText}</StatusPill>
        </div>
        <form
          className="grid grid-cols-1 gap-4 lg:grid-cols-2"
          onSubmit={(event) => {
            event.preventDefault();
            onSave();
          }}
        >
          <Field label="Base URL">
            <input
              className={inputClass}
              value={pancakeForm.baseUrl}
              onChange={(e) => onFormChange({ baseUrl: e.target.value })}
            />
          </Field>
          <Field label="Auth mode">
            <select
              className={inputClass}
              value={pancakeForm.authMode}
              onChange={(e) => onFormChange({ authMode: e.target.value })}
            >
              <option value="bearer">Bearer token</option>
              <option value="header">Custom header</option>
            </select>
          </Field>
          <Field label="Access token">
            <input
              className={inputClass}
              type="password"
              value={pancakeForm.accessToken}
              onChange={(e) => onFormChange({ accessToken: e.target.value })}
              placeholder={pancakeQuery.data?.hasAccessToken ? "Đã lưu token, nhập để thay thế" : "Nhập access token"}
            />
          </Field>
          <Field label="Webhook secret">
            <input
              className={inputClass}
              type="password"
              value={pancakeForm.webhookSecret}
              onChange={(e) => onFormChange({ webhookSecret: e.target.value })}
              placeholder={
                pancakeQuery.data?.hasWebhookSecret ? "Đã lưu secret, nhập để thay thế" : "Nhập webhook secret"
              }
            />
          </Field>
          <Field label="Signature header">
            <input
              className={inputClass}
              value={pancakeForm.signatureHeader}
              onChange={(e) => onFormChange({ signatureHeader: e.target.value })}
            />
          </Field>
          <Field label="Signature algo">
            <input
              className={inputClass}
              value={pancakeForm.signatureAlgo}
              onChange={(e) => onFormChange({ signatureAlgo: e.target.value })}
            />
          </Field>
          <Field label="Signature encoding">
            <select
              className={inputClass}
              value={pancakeForm.signatureEncoding}
              onChange={(e) => onFormChange({ signatureEncoding: e.target.value })}
            >
              <option value="hex">hex</option>
              <option value="base64">base64</option>
            </select>
          </Field>
          <Field label="Send path template">
            <input
              className={inputClass}
              value={pancakeForm.sendPathTemplate}
              onChange={(e) => onFormChange({ sendPathTemplate: e.target.value })}
            />
          </Field>
          <label className="inline-flex items-center gap-2 text-body-md font-semibold text-secondary">
            <input
              type="checkbox"
              className="size-4 accent-primary"
              checked={pancakeForm.isActive}
              onChange={(e) => onFormChange({ isActive: e.target.checked })}
            />
            Bật kết nối Pancake
          </label>
          <div className="flex flex-wrap justify-end gap-2 lg:col-span-2">
            <Button
              type="button"
              variant="outline"
              onClick={onDisconnect}
              disabled={!pancakeQuery.data || isDisconnectPending}
            >
              <span className="material-symbols-outlined text-[18px]">link_off</span>
              Ngắt kết nối
            </Button>
            <Button type="submit" disabled={isSavePending}>
              <span className="material-symbols-outlined text-[18px]">save</span>
              Lưu cấu hình
            </Button>
          </div>
        </form>
      </Card>

      <Card>
        <h2 className="text-headline-sm text-secondary">Webhook</h2>
        <p className="mt-1 text-body-md text-on-surface-variant">URL nhận event từ Pancake theo tenant slug.</p>
        <div className="mt-4 rounded-lg border border-outline bg-surface p-3">
          <p className="text-label-caps uppercase text-on-surface-variant">Tenant</p>
          <p className="mt-1 font-mono text-mono-status text-secondary">
            {webhookQuery.data?.tenantSlug ?? "—"}
          </p>
        </div>
        <div className="mt-3 rounded-lg border border-outline bg-surface p-3">
          <p className="text-label-caps uppercase text-on-surface-variant">Webhook URL</p>
          <p className="mt-1 break-all font-mono text-mono-status text-secondary">
            {webhookQuery.data?.webhookUrl ?? "Đang tải..."}
          </p>
        </div>
        <div className="mt-4">
          <Button
            type="button"
            variant="outline"
            onClick={onCopyWebhook}
            disabled={!webhookQuery.data?.webhookUrl}
          >
            <span className="material-symbols-outlined text-[18px]">content_copy</span>
            Copy URL
          </Button>
        </div>
        <div className="mt-5 space-y-2 text-body-md text-on-surface-variant">
          <p>
            Token và webhook secret được mã hóa ở backend; frontend chỉ gửi giá trị mới khi bạn nhập.
          </p>
          <p>
            Các kênh ads/lookalike đang dùng <code>/api/ads</code>; phần này chỉ quản lý kết nối Pancake.
          </p>
        </div>
      </Card>
    </section>
  );
}