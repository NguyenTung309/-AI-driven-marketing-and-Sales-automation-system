import { Alert, Button, Card, StatusPill } from "@/shared/ui";
import { errorMessage, inputClass, type BrandingForm, type PancakeForm } from "./adminHelpers";
import { Field } from "./adminUi";
import type { PancakeConfig, PancakeWebhookUrl } from "@/shared/api/admin";
import { AdminSocialChannelsSection } from "./AdminSocialChannelsSection";

interface AdminIntegrationsTabProps {
  readonly brandingForm: BrandingForm;
  readonly onUpdateBrandingForm: (patch: Partial<BrandingForm>) => void;
  readonly brandingMutationError: unknown;
  readonly brandingMutationPending: boolean;
  readonly brandingFetching: boolean;
  readonly onSubmitBranding: () => void;
  readonly pancakeForm: PancakeForm;
  readonly onUpdatePancakeForm: (patch: Partial<PancakeForm>) => void;
  readonly pancakeData: PancakeConfig | null | undefined;
  readonly pancakeMutationPending: boolean;
  readonly onSubmitPancake: () => void;
  readonly onDeletePancake: () => void;
  readonly deletePancakePending: boolean;
  readonly webhookData: PancakeWebhookUrl | undefined;
  readonly onCopyWebhook: () => void;
}

export function AdminIntegrationsTab({
  brandingForm,
  onUpdateBrandingForm,
  brandingMutationError,
  brandingMutationPending,
  brandingFetching,
  onSubmitBranding,
  pancakeForm,
  onUpdatePancakeForm,
  pancakeData,
  pancakeMutationPending,
  onSubmitPancake,
  onDeletePancake,
  deletePancakePending,
  webhookData,
  onCopyWebhook,
}: AdminIntegrationsTabProps) {
  return (
    <section className="space-y-gutter">
      <Card>
        <div className="mb-5 flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 className="text-headline-sm text-secondary">Thương hiệu đơn vị</h2>
            <p className="mt-1 text-body-md text-on-surface-variant">Tên, logo và màu hiển thị trên trang hỗ trợ khách hàng.</p>
          </div>
          <div className="flex items-center gap-2 rounded border border-outline bg-surface px-3 py-2">
            <span className="size-5 rounded" style={{ backgroundColor: brandingForm.primaryColor }} />
            <span className="size-5 rounded" style={{ backgroundColor: brandingForm.accentColor }} />
          </div>
        </div>
        {brandingMutationError ? <Alert tone="error">{errorMessage(brandingMutationError)}</Alert> : null}
        <form
          className="mt-4 grid grid-cols-1 gap-4 lg:grid-cols-2"
          onSubmit={(event) => {
            event.preventDefault();
            onSubmitBranding();
          }}
        >
          <Field label="Tên thương hiệu">
            <input className={inputClass} value={brandingForm.brandName} onChange={(event) => onUpdateBrandingForm({ brandName: event.target.value })} />
          </Field>
          <Field label="Tên hỗ trợ">
            <input className={inputClass} value={brandingForm.supportName} onChange={(event) => onUpdateBrandingForm({ supportName: event.target.value })} />
          </Field>
          <Field label="Logo hiển thị">
            <input className={inputClass} value={brandingForm.logoUrl} onChange={(event) => onUpdateBrandingForm({ logoUrl: event.target.value })} />
          </Field>
          <div className="grid grid-cols-2 gap-3">
            <Field label="Màu chính">
              <input className={`${inputClass} h-11 p-1`} type="color" value={brandingForm.primaryColor} onChange={(event) => onUpdateBrandingForm({ primaryColor: event.target.value })} />
            </Field>
            <Field label="Màu nhấn">
              <input className={`${inputClass} h-11 p-1`} type="color" value={brandingForm.accentColor} onChange={(event) => onUpdateBrandingForm({ accentColor: event.target.value })} />
            </Field>
          </div>
          <Field label="Lời chào khung chat">
            <textarea
              className={`${inputClass} min-h-24`}
              value={brandingForm.widgetGreeting}
              onChange={(event) => onUpdateBrandingForm({ widgetGreeting: event.target.value })}
            />
          </Field>
          <div className="flex items-end justify-end">
            <Button type="submit" disabled={brandingMutationPending || brandingFetching}>
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">palette</span>
              Lưu thương hiệu
            </Button>
          </div>
        </form>
      </Card>

      <div className="grid grid-cols-1 gap-gutter xl:grid-cols-[minmax(0,1fr)_420px]">
        <Card>
          <div className="mb-5 flex flex-wrap items-center justify-between gap-3">
            <div>
              <h2 className="text-headline-sm text-secondary">Kênh Pancake</h2>
              <p className="mt-1 text-body-md text-on-surface-variant">Cấu hình gửi/nhận hội thoại qua Pancake cho đơn vị hiện tại.</p>
            </div>
            <StatusPill tone={pancakeData?.isActive ? "success" : "warning"}>
              {pancakeData?.isActive ? "Hoạt động" : "Chưa bật"}
            </StatusPill>
          </div>
          <form
            className="grid grid-cols-1 gap-4 lg:grid-cols-2"
            onSubmit={(event) => {
              event.preventDefault();
              onSubmitPancake();
            }}
          >
            <Field label="Cổng Pancake">
              <input className={inputClass} value={pancakeForm.baseUrl} onChange={(event) => onUpdatePancakeForm({ baseUrl: event.target.value })} />
            </Field>
            <Field label="Cách xác thực">
              <select className={inputClass} value={pancakeForm.authMode} onChange={(event) => onUpdatePancakeForm({ authMode: event.target.value })}>
                <option value="bearer">Mã truy cập</option>
                <option value="header">Trường gửi kèm tùy chỉnh</option>
              </select>
            </Field>
            <Field label="Mã truy cập">
              <input
                className={inputClass}
                type="password"
                value={pancakeForm.accessToken}
                onChange={(event) => onUpdatePancakeForm({ accessToken: event.target.value })}
                placeholder={pancakeData?.hasAccessToken ? "Đã lưu mã, nhập để thay thế" : "Nhập mã truy cập"}
              />
            </Field>
            <Field label="Mã bí mật nhận sự kiện">
              <input
                className={inputClass}
                type="password"
                value={pancakeForm.webhookSecret}
                onChange={(event) => onUpdatePancakeForm({ webhookSecret: event.target.value })}
                placeholder={pancakeData?.hasWebhookSecret ? "Đã lưu mã bí mật, nhập để thay thế" : "Nhập mã bí mật nhận sự kiện"}
              />
            </Field>
            <Field label="Tên thông tin xác minh">
              <input className={inputClass} value={pancakeForm.signatureHeader} onChange={(event) => onUpdatePancakeForm({ signatureHeader: event.target.value })} />
            </Field>
            <Field label="Kiểu xác minh">
              <input className={inputClass} value={pancakeForm.signatureAlgo} onChange={(event) => onUpdatePancakeForm({ signatureAlgo: event.target.value })} />
            </Field>
            <Field label="Dạng mã xác minh">
              <select className={inputClass} value={pancakeForm.signatureEncoding} onChange={(event) => onUpdatePancakeForm({ signatureEncoding: event.target.value })}>
                <option value="hex">Dạng chuẩn</option>
                <option value="base64">Dạng mã hóa</option>
              </select>
            </Field>
            <Field label="Mẫu gửi tin nhắn">
              <input className={inputClass} placeholder="Nhập mẫu gửi tin do Pancake cung cấp" value={pancakeForm.sendPathTemplate} onChange={(event) => onUpdatePancakeForm({ sendPathTemplate: event.target.value })} />
            </Field>
            <label className="inline-flex items-center gap-2 text-body-md font-semibold text-secondary">
              <input
                type="checkbox"
                className="size-4 accent-primary"
                checked={pancakeForm.isActive}
                onChange={(event) => onUpdatePancakeForm({ isActive: event.target.checked })}
              />
              Bật kết nối Pancake
            </label>
            <div className="flex flex-wrap justify-end gap-2 lg:col-span-2">
              <Button type="button" variant="outline" onClick={onDeletePancake} disabled={!pancakeData || deletePancakePending}>
                <span aria-hidden="true" className="material-symbols-outlined text-[18px]">link_off</span>
                Ngắt kết nối
              </Button>
              <Button type="submit" disabled={pancakeMutationPending}>
                <span aria-hidden="true" className="material-symbols-outlined text-[18px]">save</span>
                Lưu cấu hình
              </Button>
            </div>
          </form>
        </Card>

        <Card>
          <h2 className="text-headline-sm text-secondary">Nhận tín hiệu từ Pancake</h2>
          <p className="mt-1 text-body-md text-on-surface-variant">Mã kết nối đã được tạo cho đơn vị hiện tại.</p>
          <div className="mt-4 rounded-lg border border-outline bg-surface p-3">
            <p className="text-label-caps uppercase text-on-surface-variant">Đơn vị</p>
            <p className="mt-1 font-mono text-mono-status text-secondary">{webhookData?.tenantSlug ?? "—"}</p>
          </div>
          <div className="mt-3 rounded-lg border border-outline bg-surface p-3">
            <p className="text-label-caps uppercase text-on-surface-variant">Mã kết nối</p>
            <p className="mt-1 text-body-md text-secondary">{webhookData?.webhookUrl ? "Sẵn sàng sao chép" : "Đang tải..."}</p>
          </div>
          <div className="mt-4">
            <Button type="button" variant="outline" onClick={onCopyWebhook} disabled={!webhookData?.webhookUrl}>
              <span aria-hidden="true" className="material-symbols-outlined text-[18px]">content_copy</span>
              Sao chép
            </Button>
          </div>
          <div className="mt-5 space-y-2 text-body-md text-on-surface-variant">
            <p>Mã truy cập và mã bí mật nhận sự kiện được mã hóa; giao diện quản trị chỉ gửi giá trị mới khi bạn nhập.</p>
            <p>Phần này chỉ quản lý kết nối Pancake cho đơn vị hiện tại.</p>
          </div>
        </Card>
      </div>

      <AdminSocialChannelsSection />
    </section>
  );
}
