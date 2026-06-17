import { Alert, Button, Modal } from "@/shared/ui";
import type { RoleModalMode } from "../admin.types";
import { errorMessage, inputClass } from "../admin.types";
import { Field } from "./Field";

interface RoleFormData {
  readonly name: string;
  readonly description: string;
}

interface RoleModalProps {
  readonly mode: RoleModalMode;
  readonly onClose: () => void;
  readonly form: RoleFormData;
  readonly onFormChange: (form: RoleFormData) => void;
  readonly onSubmit: () => void;
  readonly isPending: boolean;
  readonly error: unknown;
}

export function RoleModal({ mode, onClose, form, onFormChange, onSubmit, isPending, error }: RoleModalProps) {
  const title = mode === "edit" ? "Cập nhật vai trò" : "Thêm vai trò";

  return (
    <Modal
      open={mode !== null}
      onClose={onClose}
      title={title}
      footer={
        <>
          <Button type="button" variant="ghost" onClick={onClose} disabled={isPending}>
            Huỷ
          </Button>
          <Button type="submit" form="admin-role-form" disabled={isPending}>
            <span className="material-symbols-outlined text-[18px]">save</span>
            Lưu
          </Button>
        </>
      }
    >
      {error ? <Alert tone="error">{errorMessage(error)}</Alert> : null}
      <form
        id="admin-role-form"
        className="space-y-4"
        onSubmit={(event) => {
          event.preventDefault();
          onSubmit();
        }}
      >
        <Field label="Tên vai trò">
          <input
            className={inputClass}
            required
            value={form.name}
            onChange={(e) => onFormChange({ ...form, name: e.target.value })}
          />
        </Field>
        <Field label="Mô tả">
          <textarea
            className={`${inputClass} min-h-24`}
            value={form.description}
            onChange={(e) => onFormChange({ ...form, description: e.target.value })}
          />
        </Field>
      </form>
    </Modal>
  );
}