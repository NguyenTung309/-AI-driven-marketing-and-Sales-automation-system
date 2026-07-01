import { Alert, Button, Modal } from "@/shared/ui";
import { errorMessage, Field, inputClass, type AdminKeyFormState } from "./adminHelpers";

interface AdminKeyModalProps {
  readonly open: boolean;
  readonly keyForm: AdminKeyFormState;
  readonly onChange: (patch: Partial<AdminKeyFormState>) => void;
  readonly pending: boolean;
  readonly error: unknown;
  readonly onClose: () => void;
  readonly onSubmit: () => void;
}

export function AdminKeyModal({ open, keyForm, onChange, pending, error, onClose, onSubmit }: AdminKeyModalProps) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Phát hành khóa tích hợp"
      footer={
        <>
          <Button type="button" variant="ghost" onClick={onClose} disabled={pending}>Hủy</Button>
          <Button type="submit" form="admin-key-form" disabled={pending}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">vpn_key</span>
            Phát hành
          </Button>
        </>
      }
    >
      {error ? <Alert tone="error">{errorMessage(error)}</Alert> : null}
      <form
        id="admin-key-form"
        className="space-y-4"
        onSubmit={(event) => {
          event.preventDefault();
          onSubmit();
        }}
      >
        <Field label="Tên khóa">
          <input className={inputClass} required value={keyForm.name} onChange={(event) => onChange({ name: event.target.value })} />
        </Field>
        <Field label="Quyền truy cập">
          <textarea className={`${inputClass} min-h-24`} value={keyForm.scopes} onChange={(event) => onChange({ scopes: event.target.value })} />
        </Field>
        <Field label="Ngày hết hạn">
          <input className={inputClass} type="date" value={keyForm.expiresAt} onChange={(event) => onChange({ expiresAt: event.target.value })} />
        </Field>
      </form>
    </Modal>
  );
}
