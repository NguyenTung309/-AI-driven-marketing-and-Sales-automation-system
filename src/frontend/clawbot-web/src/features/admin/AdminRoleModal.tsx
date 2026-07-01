import { Alert, Button, Modal } from "@/shared/ui";
import { errorMessage, Field, inputClass, type AdminRoleFormState } from "./adminHelpers";

export type RoleModalMode = "create" | "edit" | null;

interface AdminRoleModalProps {
  readonly mode: RoleModalMode;
  readonly roleForm: AdminRoleFormState;
  readonly onChange: (patch: Partial<AdminRoleFormState>) => void;
  readonly pending: boolean;
  readonly error: unknown;
  readonly onClose: () => void;
  readonly onSubmit: () => void;
}

export function AdminRoleModal({ mode, roleForm, onChange, pending, error, onClose, onSubmit }: AdminRoleModalProps) {
  return (
    <Modal
      open={mode !== null}
      onClose={onClose}
      title={mode === "edit" ? "Cập nhật vai trò" : "Thêm vai trò"}
      footer={
        <>
          <Button type="button" variant="ghost" onClick={onClose} disabled={pending}>Hủy</Button>
          <Button type="submit" form="admin-role-form" disabled={pending}>
            <span aria-hidden="true" className="material-symbols-outlined text-[18px]">save</span>
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
          <input className={inputClass} required value={roleForm.name} onChange={(event) => onChange({ name: event.target.value })} />
        </Field>
        <Field label="Mô tả">
          <textarea className={`${inputClass} min-h-24`} value={roleForm.description} onChange={(event) => onChange({ description: event.target.value })} />
        </Field>
      </form>
    </Modal>
  );
}
