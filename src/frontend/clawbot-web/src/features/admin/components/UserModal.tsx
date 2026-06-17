import { Alert, Button, Modal } from "@/shared/ui";
import type { Role, UserModalMode } from "../admin.types";
import { errorMessage, inputClass } from "../admin.types";
import { Field } from "./Field";

interface UserFormData {
  readonly displayName: string;
  readonly email: string;
  readonly password: string;
  readonly isActive: boolean;
  roles: string[];
}

interface UserModalProps {
  readonly mode: UserModalMode;
  readonly onClose: () => void;
  readonly form: UserFormData;
  readonly roles: readonly Role[];
  readonly onFormChange: (form: UserFormData) => void;
  readonly onToggleRole: (name: string) => void;
  readonly onSubmit: () => void;
  readonly isPending: boolean;
  readonly error: unknown;
}

export function UserModal({
  mode,
  onClose,
  form,
  roles,
  onFormChange,
  onToggleRole,
  onSubmit,
  isPending,
  error,
}: UserModalProps) {
  const isCreate = mode === "create";

  const title = isCreate ? "Thêm người dùng" : "Cập nhật người dùng";

  return (
    <Modal
      open={mode !== null}
      onClose={onClose}
      title={title}
      footer={
        <>
          <Button type="button" variant="ghost" onClick={onClose} disabled={isPending}>
            Hủy
          </Button>
          <Button type="submit" form="admin-user-form" disabled={isPending}>
            <span className="material-symbols-outlined text-[18px]">save</span>
            Lưu
          </Button>
        </>
      }
    >
      {error ? <Alert tone="error">{errorMessage(error)}</Alert> : null}
      <form
        id="admin-user-form"
        className="space-y-4"
        onSubmit={(event) => {
          event.preventDefault();
          onSubmit();
        }}
      >
        <Field label="Tên hiển thị">
          <input
            className={inputClass}
            required
            value={form.displayName}
            onChange={(e) => onFormChange({ ...form, displayName: e.target.value })}
          />
        </Field>
        {isCreate ? (
          <>
            <Field label="Email">
              <input
                className={inputClass}
                required
                type="email"
                value={form.email}
                onChange={(e) => onFormChange({ ...form, email: e.target.value })}
              />
            </Field>
            <Field label="Mật khẩu">
              <input
                className={inputClass}
                required
                type="password"
                minLength={8}
                value={form.password}
                onChange={(e) => onFormChange({ ...form, password: e.target.value })}
              />
            </Field>
            <div>
              <p className="mb-2 text-label-sm font-semibold text-secondary">Vai trò ban đầu</p>
              <div className="flex flex-wrap gap-2">
                {roles.map((role) => (
                  <label
                    key={role.id}
                    className="inline-flex items-center gap-2 rounded border border-outline px-3 py-2 text-body-md"
                  >
                    <input
                      type="checkbox"
                      className="size-4 accent-primary"
                      checked={form.roles.includes(role.name)}
                      onChange={() => onToggleRole(role.name)}
                    />
                    {role.name}
                  </label>
                ))}
              </div>
            </div>
          </>
        ) : (
          <label className="inline-flex items-center gap-2 text-body-md font-semibold text-secondary">
            <input
              type="checkbox"
              className="size-4 accent-primary"
              checked={form.isActive}
              onChange={(e) => onFormChange({ ...form, isActive: e.target.checked })}
            />
            Người dùng đang hoạt động
          </label>
        )}
      </form>
    </Modal>
  );
}

