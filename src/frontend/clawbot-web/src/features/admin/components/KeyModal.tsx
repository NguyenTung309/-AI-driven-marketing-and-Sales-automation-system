import { Alert, Button, Modal } from "@/shared/ui";
import { errorMessage, inputClass } from "../admin.types";
import { Field } from "./Field";

interface KeyFormData {
  readonly name: string;
  readonly scopes: string;
  readonly expiresAt: string;
}

interface KeyModalProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly form: KeyFormData;
  readonly onFormChange: (form: KeyFormData) => void;
  readonly onSubmit: () => void;
  readonly isPending: boolean;
  readonly error: unknown;
}

export function KeyModal({
  open,
  onClose,
  form,
  onFormChange,
  onSubmit,
  isPending,
  error,
}: KeyModalProps) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Phát hành API key"
      footer={
        <>
          <Button type="button" variant="ghost" onClick={onClose} disabled={isPending}>
            Hủy
          </Button>
          <Button type="submit" form="admin-key-form" disabled={isPending}>
            <span className="material-symbols-outlined text-[18px]">vpn_key</span>
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
        <Field label="Tên key">
          <input
            className={inputClass}
            required
            value={form.name}
            onChange={(e) => onFormChange({ ...form, name: e.target.value })}
          />
        </Field>

        <Field label="Scopes">
          <textarea
            className={`${inputClass} min-h-24`}
            value={form.scopes}
            onChange={(e) => onFormChange({ ...form, scopes: e.target.value })}
          />
        </Field>

        <Field label="Ngày hết hạn">
          <input
            className={inputClass}
            type="date"
            value={form.expiresAt}
            onChange={(e) => onFormChange({ ...form, expiresAt: e.target.value })}
          />
        </Field>
      </form>
    </Modal>
  );
}